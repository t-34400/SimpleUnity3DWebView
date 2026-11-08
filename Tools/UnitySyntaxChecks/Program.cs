using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0])) return 2;
        var files = Directory.GetFiles(args[0], "*.cs", SearchOption.AllDirectories);
        if (files.Length == 0) { Console.Error.WriteLine("No C# sources found"); return 1; }
        int errors = 0;
        foreach (string path in files.OrderBy(value => value)) {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp9), path);
            foreach (var error in tree.GetDiagnostics().Where(value => value.Severity == DiagnosticSeverity.Error)) {
                Console.Error.WriteLine(error); errors++;
            }
        }
        Console.WriteLine("Parsed " + files.Length + " C# files (C# 9 syntax only; no Unity type resolution or Editor compile)");
        return errors == 0 ? 0 : 1;
    }
}
