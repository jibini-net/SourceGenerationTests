﻿namespace SourceGenerator;

/*
 * Implements a naive Finite State Automaton which supports nondeterminism
 * through epsilon transitions. Each state of the machine has a mapping of next
 * states, keyed against characters, but also a set of epsilon transitions
 * which can be reached with no character actions.
 * 
 * The longest valid match from any merged FSA will be returned. In the case of
 * ambiguous tokens, the token of highest precedence (lowest ID) will match.
 */
[Serializable]
public class Fsa
{
    /*
     * Debug value indicating the character used to arrive in this state.
     */
    public char Letter { get; private set; } = '\0';

    public Fsa()
    {
    }

    public Fsa(char letter)
    {
        Letter = letter;
    }

    /*
     * Set of transitions for particular letters; if all transitions are put
     * here, the FSA will be deterministic.
     */
    public Dictionary<char, Fsa> Next { get; private set; } = new();

    /*
     * IDs of tokens which are accepted if this state is reached during a match.
     */
    public List<int> Accepts { get; private set; } = new();

    /*
     * States which can be reached by taking no action, and are reached if the
     * parent state ("this") is reached.
     */
    public List<Fsa> Epsilon { get; private set; } = new();

    /*
     * Creates new nodes in the FSA to match the provided word. The resulting
     * machine is likely nondeterministic, depending on which regular expression
     * is provided and any logical "ORs" or ambiguous tokens.
     * 
     * Paths are not reused nor optimized at this stage. If a letter is already
     * in the "next" list of a state, it is added as an epsilon transition.
     */
    public Task Build(string word, int accept, out List<Fsa> frontier, Func<int, Task> cb)
    {
        var _f = frontier = new() { this };
        return Task.Run(async () =>
        {
            // State which will be restored to when using "+" expression operator
            var restoreTo = this;
            var parensDepth = 0;
            var isEscaped = false;

            for (var regIndex = 0; regIndex < word.Length; regIndex++)
            {
                var c = word[regIndex];
                var notifyAsync = async () =>
                {
                    if (parensDepth == 0)
                    {
                        await cb.Invoke(regIndex);
                    }
                };

                if (parensDepth > 0 || (c == ')' && !isEscaped))
                {
                    // We are currently "within parentheses;" discard chars
                    switch (c)
                    {
                        case '(':
                            parensDepth++;
                            break;

                        case ')':
                            if (--parensDepth < 0) goto outer_break;
                            else break;
                    }
                    // Discard all characters, including last balanced ')'
                    continue;
                }

                if (!isEscaped)
                {
                    switch (c)
                    {
                        case '\\':
                            isEscaped = true;
                            await notifyAsync();
                            continue;

                        case '|':
                            {
                                var subExpr = new Fsa();
                                Epsilon.Add(subExpr);
                                await subExpr.Build(word.Substring(regIndex + 1), accept, out var _frontier,
                                    async (i) => await cb.Invoke(regIndex + i + 1));
                                // Merge the "ORed" frontier with the parent frontier
                                _f.AddRange(_frontier);
                            }
                            goto outer_break;

                        case '(':
                            {
                                // Enter parentheses discarding mode
                                parensDepth++;
                                var subExpr = (restoreTo = new Fsa());
                                // Merge all states to parentheses using eps transitions
                                foreach (var state in _f)
                                {
                                    state.Epsilon.Add(subExpr);
                                }
                                await subExpr.Build(word.Substring(regIndex + 1), 0, out var __f,
                                    async (i) => await cb.Invoke(regIndex + i + 1));
                                _f.Clear();
                                _f.AddRange(__f);
                            }
                            await notifyAsync();
                            continue;

                        case '+':
                            foreach (var state in _f)
                            {
                                state.Epsilon.Add(restoreTo);
                            }
                            await notifyAsync();
                            continue;
                    }
                }
                // Any non-escaping character resets to not being escaped
                isEscaped = false;

                // Always create a new node, which will likely become nondeterministic
                var useState = new Fsa(c);
                // Creates intermediate node to avoid infinite cyclic flow
                restoreTo = new Fsa();
                restoreTo.Next[c] = useState;

                foreach (var state in _f)
                {
                    if (state.Next.ContainsKey(c))
                    {
                        // Tokens are nondeterministic via eps transitions
                        state.Epsilon.Add(restoreTo);
                    } else
                    {
                        state.Next[c] = useState;
                    }
                }
                // After appending a char, frontier is always one merged branch
                //frontier = new() { useState };
                _f.Clear();
                _f.Add(useState);
                await notifyAsync();
            }
        outer_break:

            if (accept > 0)
            {
                foreach (var state in _f)
                {
                    // If there are already elements, tokens may be ambiguous
                    state.Accepts.Add(accept);
                }
            }
        });
    }

    /*
     * Creates new nodes in the FSA to match the provided word. The resulting
     * machine is likely nondeterministic, depending on which regular expression
     * is provided and any logical "ORs" or ambiguous tokens.
     * 
     * Paths are not reused nor optimized at this stage. If a letter is already
     * in the "next" list of a state, it is added as an epsilon transition.
     */
    public async Task Build(string word, int accept, Func<int, Task> cb)
    {
        await Build(word, accept, out var _, cb);
    }

    /*
     * Finds all states accessible from this state without consuming any
     * characters, and also any states recursively accessible thereunder.
     */
    protected IEnumerable<Fsa> EpsilonClosure()
    {
        return new[] { this }
            .Concat(Epsilon)
            .Concat(Epsilon.SelectMany((it) => it.EpsilonClosure()));
    }

    /*
     * Single- or zero-element list of reachable deterministic states.
     */
    protected IEnumerable<Fsa> AdjacentSet(char c)
    {
        if (Next.ContainsKey(c))
        {
            yield return Next[c];
        }
        yield break;
    }

    /*
     * Traverses the DFA in a breadth-first fashion, allowing vectorized
     * traversal of a frontier in case of nondeterministic automata.
     * 
     * A "frontier" refers to the set of nodes currently being visited. An
     * "epsilon closure" refers to nodes related to the frontier (and the
     * frontier itself) accessible without consuming any characters. Acceptance
     * states are achieved if any node on the frontier or any node in the
     * resulting epsilon closure has a token ID in its accept list.
     * 
     * Any reached accept state will update the "longest end" tracker, and the
     * last recorded longest match is returned on the first invalid state.
     */
    public (int accepted, string match) Search(string text, int startIndex)
    {
        var closure = EpsilonClosure().Distinct().ToList();
        int textIndex = startIndex, longestEnd = -1, match = 0;

        for (; ; )
        {
            // Any accept state in the frontier is a valid match
            var acceptState = closure.Where((it) => it.Accepts.Count > 0).FirstOrDefault();
            if (acceptState is not null)
            {
                longestEnd = textIndex;
                match = acceptState.Accepts.Min();
            }

            // "Invalid state" due to end of input or lack of next states
            if (textIndex >= text.Length || closure.Count == 0)
            {
                break;
            }
            var c = text[textIndex++];
            var frontier = closure.SelectMany((it) => it.AdjacentSet(c)).Distinct();
            closure = frontier.SelectMany((it) => it.EpsilonClosure()).Distinct().ToList();
        }

        if (longestEnd == -1)
        {
            return (0, "");
        } else
        {
            return (match, text.Substring(startIndex, longestEnd - startIndex));
        }
    }

    /*
     * Accessible nodes from this one, ignoring epsilon transitions from here.
     */
    protected Dictionary<char, List<Fsa>> memoizedClosures = new();

    /*
     * Returns a cached or calculated list of states accessible from this one
     * after applying the character transition. Only checks epsilon on children.
     */
    protected List<Fsa> AccessibleMemoized(char c)
    {
        return memoizedClosures.TryGetValue(c, out var cached)
            ? cached
            : (memoizedClosures[c] = AdjacentSet(c)
                .SelectMany((it) => it.EpsilonClosure())
                .Distinct()
                .ToList());
    }

    /*
     * Performs an expensive conversion between NFA and DFA which calculates the
     * epsilon closures at all states for all characters in the alphabet. State
     * is calculated and cached during runtime, which renders the FSA invalid if
     * any of the structure is later modified.
     * 
     * Do not modify the NFA again after calling the conversion to DFA; the NFA
     * would continue to function, but this method would not.
     */
    public async Task<Fsa> ConvertToDfa(Func<Queue<(Fsa node, List<Fsa> closure)>, Fsa, Task> cb)
    {
        var result = new Fsa()
        {
            Letter = Letter,
            Accepts = new(Accepts)
        };
        var queue = new Queue<(Fsa node, List<Fsa> closure)>();
        // Visited set for cycles and already-deterministic nodes
        var replace = new Dictionary<HashSet<Fsa>, Fsa>(HashSet<Fsa>.CreateSetComparer());

        queue.Enqueue((result, EpsilonClosure().Distinct().ToList()));
        await cb.Invoke(queue, result);
        do
        {
            var (node, oldClosure) = queue.Dequeue();
            // Find all actions which can be taken from this state
            var alphabet = oldClosure.SelectMany((it) => it.Next.Keys).Distinct().ToList();

            // Find all nodes accessible from all discovered characters
            var closures = alphabet.ToDictionary(
                (c) => c,
                (c) => oldClosure.SelectMany((it) => it.AccessibleMemoized(c)).ToList());
            
            foreach (var (c, closure) in closures)
            {
                var withLetters = closure.Where((it) => it.Letter == c).ToHashSet();
                // Find an existing state for target nodes
                if (replace.TryGetValue(withLetters, out var cached))
                {
                    node.Next[c] = cached;
                    continue;
                }

                var created = new Fsa()
                {
                    Letter = c,
                    // Merged node will accept any tokens accepted by originals
                    Accepts = closure.SelectMany((it) => it.Accepts).Distinct().ToList()
                };
                node.Next[c] = created;
                replace[withLetters] = created;

                queue.Enqueue((created, closure.ToList()));
                await cb.Invoke(queue, result);
            }
        } while (queue.Count > 0);

        return result;
    }
}