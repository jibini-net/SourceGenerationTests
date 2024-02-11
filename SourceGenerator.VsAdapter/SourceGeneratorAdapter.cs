﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGenerator.VsAdapter
{
    [Generator]
    public class SourceGeneratorAdapter : ISourceGenerator
    {
        public const string SERVER_IP = "127.0.0.1";
        public const int PORT = 58994;

        public static MemoryStream GenerateSource(AdditionalText file)
        {
            var ip = new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT);
            using (var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Connect(ip);

                var encodedName = Path.GetFileName(file.Path)
                    .Replace("\\", "\\\\")
                    .Replace("{", "\\{")
                    .Replace("}", "\\}");
                socket.Send(Encoding.UTF8.GetBytes($"{{{encodedName}}} "));
                socket.Send(Encoding.UTF8.GetBytes(file.GetText().ToString()));
                socket.Send(new byte[] { 0x00 });

                var recvBuffer = new byte[2048];
                var sourceText = new MemoryStream();

                for (;;)
                {
                    var readBytes = socket.Receive(recvBuffer);
                    if (readBytes == 0)
                    {
                        break;
                    }
                    sourceText.Write(recvBuffer, 0, readBytes);
                }

                //TODO Propagate exceptions
                return sourceText;
            }
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var findExts = new[] { ".model", ".view" }.ToImmutableHashSet();
            var files = context.AdditionalFiles
                .Where((it) => findExts.Contains(
                    Path.GetExtension(it.Path).ToLowerInvariant()))
                .ToList();
            var completed = new SemaphoreSlim(0, files.Count);
            var sources = new BlockingCollection<(string file, MemoryStream source)>();

            foreach (var file in files)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var name = Path.GetFileName(file.Path);
                        var source = GenerateSource(file);

                        sources.Add(($"{name}.g.cs", source));
                    } catch (Exception ex)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SG001",
                                "",
                                ex.Message,
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            null));
                    } finally
                    {
                        completed.Release();
                    }
                });
            }
            for (var i = 0; i < files.Count; i ++)
            {
                completed.Wait();
            }

            // Add all sources from main thread for safety
            //context.AddSource("_Includes.g.cs", INCLUDES);
            context.AddSource("_ServiceCollection.g.cs", GenerateServiceCollection(files));
            context.AddSource("_ViewCollection.g.cs", GenerateViewCollection(files));
            foreach (var source in sources)
            {
                var sourceText = SourceText.From(source.source, Encoding.UTF8, canBeEmbedded: true);
                context.AddSource(source.file, sourceText);
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        private static string GenerateServiceCollection(List<AdditionalText> files)
        {
            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine("namespace Generated;");
            sourceBuilder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            foreach (var file in files.Where((it) => Path.GetExtension(it.Path).ToLowerInvariant() == ".model"))
            {
                var name = Path.GetFileNameWithoutExtension(file.Path);
                sourceBuilder.AppendFormat("public static class {0}Extensions\n{{\n", name);

                sourceBuilder.AppendFormat("    public static void Add{0}Backend<T>(this IServiceCollection services)\n", name);
                sourceBuilder.AppendFormat("        where T : class, {0}.IBackendService\n    {{\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.Repository>();\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.IBackendService, T>();\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.DbService>();\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.IService>((sp) => sp.GetRequiredService<{1}.DbService>());\n",
                    name,
                    name);
                sourceBuilder.AppendLine("    }");

                sourceBuilder.AppendFormat("    public static void Add{0}Frontend(this IServiceCollection services)\n    {{\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.ApiService>();\n", name);
                sourceBuilder.AppendFormat("        services.AddScoped<{0}.IService>((sp) => sp.GetRequiredService<{1}.ApiService>());\n",
                    name,
                    name);
                sourceBuilder.AppendLine("    }");

                sourceBuilder.AppendLine("}");
            }
            return sourceBuilder.ToString();
        }

        private static string GenerateViewCollection(List<AdditionalText> files)
        {
            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine("namespace Generated;");
            sourceBuilder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            foreach (var file in files.Where((it) => Path.GetExtension(it.Path).ToLowerInvariant() == ".view"))
            {
                var name = Path.GetFileNameWithoutExtension(file.Path);
                sourceBuilder.AppendFormat("public static class {0}Extensions\n{{\n", name);

                sourceBuilder.AppendFormat("    public static void Add{0}View<T>(this IServiceCollection services)\n", name);
                sourceBuilder.AppendFormat("        where T : class, {0}Base.IView\n    {{\n", name);
                sourceBuilder.AppendFormat("        services.AddTransient<T>();\n", name);
                sourceBuilder.AppendFormat("        services.AddTransient<{0}Base.IView>((sp) => sp.GetRequiredService<T>());\n",
                    name);
                sourceBuilder.AppendLine("    }");

                sourceBuilder.AppendLine("}");
            }
            return sourceBuilder.ToString();
        }
    }
}
