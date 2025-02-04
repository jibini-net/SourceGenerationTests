﻿using System.Web;

namespace SourceGenerator.Grammar;

using static Token;
using static ClassType;

/*
 * Implementation of source generation and semantic evaluation. The parser
 * operates top-down using recursive descent.
 */
public class ServiceGrammar
{
    public struct Dto
    {
        public string ModelName { get; set; }
        public List<ActionGrammar.Dto> Actions { get; set; }
    }

    public static Dto Match(TokenStream stream, string modelName, Dictionary<string, List<FieldGrammar.Dto>> splats)
    {
        var result = new Dto()
        {
            ModelName = modelName,
            Actions = []
        };

        // "service" "{"
        Program.StartSpan(TopLevel);
        stream.Poll();
        if (stream.Poll() != (int)LCurly)
        {
            throw new Exception($"Expected left curly");
        }
        Program.EndSpan();

        while (stream.Next != (int)RCurly)
        {
            // {action name} "(" {parameter list} ")" ["=>" {return type}]
            var action = ActionGrammar.Match(stream, splats);
            if (action.IsJson)
            {
                throw new Exception("JSON is not valid for service action");
            }
            result.Actions.Add(action);

            // ","
            Program.StartSpan(TopLevel);
            if (stream.Next != (int)RCurly && stream.Poll() != (int)Comma)
            {
                throw new Exception("Expected comma or '}'");
            }
            Program.EndSpan();
        }

        // "}"
        Program.StartSpan(TopLevel);
        stream.Poll();
        Program.EndSpan();

        return result;
    }

    public static void WriteServiceInterface(Dto dto)
    {
        Program.AppendLine("    public interface IService");
        Program.AppendLine("    {{");

        foreach (var action in dto.Actions)
        {
            var args = string.IsNullOrEmpty(action.SplatFrom)
                ? string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"))
                : $"{action.SplatFrom} splat";
            Program.AppendLine("        {0} {1}({2});",
                action.ReturnType == "void" ? "Task" : $"Task<{action.ReturnType}>",
                action.Name,
                args);
        }

        Program.AppendLine("    }}");
        Program.AppendLine("    public interface IBackendService : IService");
        Program.AppendLine("    {{");
        Program.AppendLine("        // Implement and inject this interface as a separate service");
        Program.AppendLine("    }}");
    }

    public static void WriteDbService(Dto dto)
    {
        Program.AppendLine("    public class DbService : IService");
        Program.AppendLine("    {{");
        Program.AppendLine("        private readonly IModelDbWrapper wrapper;");
        Program.AppendLine("        private readonly IBackendService impl;");
        Program.AppendLine("        public DbService(IModelDbWrapper wrapper, IBackendService impl)");
        Program.AppendLine("        {{");
        Program.AppendLine("            this.wrapper = wrapper;");
        Program.AppendLine("            this.impl = impl;");
        Program.AppendLine("        }}");

        foreach (var action in dto.Actions)
        {
            var args = string.IsNullOrEmpty(action.SplatFrom)
                ? string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"))
                : $"{action.SplatFrom} splat";
            Program.AppendLine("        public async {0} {1}({2})",
                action.ReturnType == "void" ? "Task" : $"Task<{action.ReturnType}>",
                action.Name,
                args);
            Program.AppendLine("        {{");

            if (action.ReturnType == "void")
            {
                Program.AppendLine("            await wrapper.ExecuteAsync(async () => await impl.{0}(", action.Name);
            } else
            {
                Program.AppendLine("            return await wrapper.ExecuteAsync<{0}>(async () => await impl.{1}(",
                    action.ReturnType,
                    action.Name);
            }
            if (string.IsNullOrEmpty(action.SplatFrom))
            {
                foreach (var par in action.Params.Select((it) => it.name))
                {
                    if (par != action.Params.First().name)
                    {
                        Program.AppendLine(",");
                    }
                    Program.Append("                ");
                    Program.Append(par);
                }
            } else
            {
                Program.Append("                splat");
            }
            Program.AppendLine("\n                ));");
            Program.AppendLine("        }}");
        }

        Program.AppendLine("    }}");
    }

    public static void WriteApiService(Dto dto)
    {
        Program.AppendLine("    public class ApiService : IService");
        Program.AppendLine("    {{");
        Program.AppendLine("        private readonly IModelApiAdapter api;");
        Program.AppendLine("        public ApiService(IModelApiAdapter api)");
        Program.AppendLine("        {{");
        Program.AppendLine("            this.api = api;");
        Program.AppendLine("        }}");

        foreach (var action in dto.Actions)
        {
            var args = string.IsNullOrEmpty(action.SplatFrom)
                ? string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"))
                : $"{action.SplatFrom} splat";
            Program.AppendLine("        public async {0} {1}({2})",
                action.ReturnType == "void" ? "Task" : $"Task<{action.ReturnType}>",
                action.Name,
                args);
            Program.AppendLine("        {{");

            //if (action.Api.Count > 0)
            //{

            if (action.ReturnType == "void")
            {
                Program.AppendLine("            await api.ExecuteAsync(\"{0}/{1}\", new\n            {{",
                    dto.ModelName,
                    action.Name);
            } else
            {
                Program.AppendLine("            return await api.ExecuteAsync<{0}>(\"{1}/{2}\", new\n            {{",
                    action.ReturnType,
                    dto.ModelName,
                    action.Name);
            }
            foreach (var (par, i) in action.Params.Select((it, i) => (it.name, i)))
            {
                if (i != 0)
                {
                    Program.AppendLine(",");
                }
                Program.Append("                ");
                if (!string.IsNullOrEmpty(action.SplatFrom))
                {
                    Program.Append("splat.");
                }
                Program.Append(par);
            }
            Program.AppendLine("\n            }});");

            //} else
            //{
            //    Program.AppendLine($"            throw new NotImplementedException(\"'{action.Name}' is not exposed via the API\");");
            //}

            Program.AppendLine("        }}");
        }

        Program.AppendLine("    }}");
    }

    public static void WriteApiControllers(Dto dto)
    {
        var versions = dto.Actions
            .SelectMany((it) => it.Api.Select((_it) => (action: it, api: _it)))
            .GroupBy(it => it.api.VersionName);

        foreach (var version in versions)
        {
            Program.AppendLine("[ApiController]");
            Program.AppendLine("[Authorize]");
            //TODO Validate version names
            Program.AppendLine("[Route(\"api/v{{version:apiVersion}}/{0}/[action]\")]",
                dto.ModelName);
            Program.AppendLine("[ControllerName(\"{0}\")]",
                dto.ModelName);
            Program.AppendLine("[ApiVersion(\"{0}\")]",
                version.Key);
            Program.AppendLine("public partial class {0}Controller_v{1} : ControllerBase",
                dto.ModelName,
                version.Key);
            Program.AppendLine("{{");

            Program.AppendLine("    private readonly {0}.IService service;",
                dto.ModelName);
            Program.AppendLine("    public {0}Controller_v{1}({0}.IService service)\n    {{",
                dto.ModelName,
                version.Key);
            Program.AppendLine("        this.service = service;");
            Program.AppendLine("    }}");

            foreach (var (action, api) in version)
            {
                Program.AppendLine("    ///<summary>{0}</summary>",
                    HttpUtility.HtmlEncode(api.Summary));
                Program.AppendLine("    [HttpPost]");
                if (action.ReturnType != "void")
                {
                    Program.AppendLine("    [Produces(typeof({0}))]",
                        action.ReturnType);
                }
                Program.AppendLine("    {0}",
                    api.Annotations);

                var args = string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"));
                Program.AppendLine("    public async Task<IActionResult> {0}({1})\n    {{",
                    action.Name,
                    args);

                Program.AppendLine("        try\n        {{");

                if (action.ReturnType == "void")
                {
                    Program.Append("            await service.{0}(",
                        action.Name);
                } else
                {
                    Program.Append("            var result = await service.{0}(", 
                        action.Name);
                }
                if (!string.IsNullOrEmpty(action.SplatFrom))
                {
                    Program.AppendLine("new {0}()\n            {{",
                        action.SplatFrom);
                } else if (action.Params.Count > 0)
                {
                    Program.AppendLine("");
                }
                foreach (var (par, i) in action.Params.Select((it, i) => (it.name, i)))
                {
                    if (i != 0)
                    {
                        Program.AppendLine(",");
                    }
                    Program.Append("                ");
                    if (!string.IsNullOrEmpty(action.SplatFrom))
                    {
                        Program.Append("{0} = ",
                            par);
                    }
                    Program.Append(par);
                }
                if (!string.IsNullOrEmpty(action.SplatFrom))
                {
                    Program.Append("\n            }}");
                }
                Program.AppendLine(");");

                if (action.ReturnType == "void")
                {
                    Program.AppendLine("            return Ok();");
                } else
                {
                    Program.AppendLine("            return Ok(result);");
                }
                Program.AppendLine("        }} catch (Exception ex)");
                Program.AppendLine("        {{");
                Program.AppendLine("            return Problem(ex.Message);");
                Program.AppendLine("        }}");
                Program.AppendLine("    }}");
            }

            Program.AppendLine("}}");
        }
    }

    public static void WriteViewInterface(Dto dto)
    {
        Program.AppendLine("    public interface IView : IRenderView\n    {{");

        foreach (var action in dto.Actions)
        {
            var args = string.IsNullOrEmpty(action.SplatFrom)
                ? string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"))
                : $"{action.SplatFrom} splat";
            Program.AppendLine("        {0} {1}({2});",
                action.ReturnType,
                action.Name,
                args);
        }

        Program.AppendLine("    }}");

        foreach (var action in dto.Actions)
        {
            var args = string.IsNullOrEmpty(action.SplatFrom)
                ? string.Join(',', action.Params.Select((it) => $"{it.type} {it.name}"))
                : $"{action.SplatFrom} splat";
            Program.AppendLine("    public abstract {0} {1}({2});",
                action.ReturnType,
                action.Name,
                args);
        }

        if (dto.Actions.Count == 0)
        {
            Program.AppendLine("    public class Default : {0}Base\n    {{",
                dto.ModelName);
            Program.AppendLine("        public Default(IServiceProvider sp) : base(sp)\n        {{\n        }}");
            Program.AppendLine("    }}");
        }
    }

    public static void WriteViewRenderer(string renderContent, string modelName)
    {
        Program.AppendLine("    public async Task<string> RenderAsync(StateDump state, int indexByTag = 0)\n    {{");
        Program.AppendLine("        var build = new StringBuilder();");
        Program.AppendLine("        var tagCounts = new Dictionary<string, int>();");
        Program.AppendLine("        state.Tag = \"{0}\";",
            modelName);
        Program.AppendLine("        state.State = GetState();");
        Program.AppendLine("        using var writer = new StringWriter(build);");

        Program.Append("{0}", renderContent);

        Program.AppendLine("        state.Trim(tagCounts);");

        Program.AppendLine("        return build.ToString();");
        Program.AppendLine("    }}");
    }

    public static void WriteViewController(Dto dto)
    {
        Program.AppendLine("[Controller]");
        Program.AppendLine("[Route(\"view/{0}\")]",
            dto.ModelName);
        Program.AppendLine("[ApiExplorerSettings(IgnoreApi = true)]");
        Program.AppendLine("public class {0}ViewController : ControllerBase",
            dto.ModelName);
        Program.AppendLine("{{");

        Program.AppendLine("    private readonly {0}Base.IView component;",
            dto.ModelName);
        Program.AppendLine("    private readonly IServiceProvider sp;");
        Program.AppendLine("    public {0}ViewController({0}Base.IView component, IServiceProvider sp)\n    {{",
            dto.ModelName);
        Program.AppendLine("        this.component = component;");
        Program.AppendLine("        this.sp = sp;");
        Program.AppendLine("    }}");

        Program.AppendLine("    [HttpPost(\"\")]");
        Program.AppendLine("    public async Task<IActionResult> Index()\n    {{");
        Program.AppendLine("        var state = await JsonSerializer.DeserializeAsync<StateDump>(Request.Body);");
        Program.AppendLine("        var html = await component.RenderPageAsync(state);");
        Program.AppendLine("        return Content(html, \"text/html\");");
        Program.AppendLine("    }}");

        foreach (var action in dto.Actions)
        {
            static string attr((string type, string name) it) => $"public {it.type} {it.name} {{ get; set; }}";
            var attrs = action.Params.Select(attr);
            var attributes = string.Join("\n        ", attrs);

            Program.AppendLine("    public class _{0}_Params\n    {{",
                action.Name);
            Program.AppendLine("        {0}",
                attributes);
            Program.AppendLine("    }}");

            Program.AppendLine("    [HttpPost(\"{0}\")]",
                action.Name);
            Program.AppendLine("    public async Task<IActionResult> {0}()\n    {{",
                action.Name);
            Program.AppendLine("        var render = await JsonSerializer.DeserializeAsync<TagRenderRequest>(Request.Body);");
            Program.AppendLine("        var pars = JsonSerializer.Deserialize<_{0}_Params>(render.Pars);",
                action.Name);
            Program.AppendLine("        var html = await component.RenderComponentAsync(sp, render.State, render.Path, async (it) =>");
            Program.AppendLine("        {{");

            Program.AppendLine("            {0}it.{1}({2});",
                (action.ReturnType == "Task" || action.ReturnType.StartsWith("Task<"))
                    ? "await "
                    : "",
                action.Name,
                string.Join(", ", action.Params.Select((it) => $"pars.{it.name}")));

            Program.AppendLine("            await Task.CompletedTask;");
            Program.AppendLine("        }});");
            Program.AppendLine("        return Content(html, \"text/html\");");
            Program.AppendLine("    }}");
        }

        Program.AppendLine("}}");
    }
}
