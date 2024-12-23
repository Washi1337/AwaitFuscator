﻿using System.CommandLine;
using System.Reflection;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AwaitFuscator.Engine;

namespace AwaitFuscator;

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(@"                      _ _     ______                   _              ____    ");
        Console.WriteLine(@"                     (_) |   |  ____|                 | |            / /\ \ _");
        Console.WriteLine(@"   __ ___      ____ _ _| |_  | |__ _   _ ___  ___ __ _| |_ ___  _ __| |  | (_)");
        Console.WriteLine(@"  / _` \ \ /\ / / _` | | __| |  __| | | / __|/ __/ _` | __/ _ \| '__| |  | |");
        Console.WriteLine(@" | (_| |\ V  V / (_| | | |_  | |  | |_| \__ \ (_| (_| | || (_) | |  | |  | |_");
        Console.WriteLine(@"  \__,_| \_/\_/ \__,_|_|\__| |_|   \__,_|___/\___\__,_|\__\___/|_|  | |  | ( )");
        Console.WriteLine(@"                                                                     \_\/_/|/");

        Console.WriteLine("Version:   {0}", Assembly.GetExecutingAssembly().GetName().Version);
        Console.WriteLine("Copyright: Washi 2024 (https://washi.dev/)");
        Console.WriteLine();

        var rootCommand = new RootCommand();
        var path = new Argument<string>(
            "path",
            "The path of the managed executable to awaitfuscate."
        );

        var types = new Option<string[]>(
            "--only-types",
            description: "Only process the specified types",
            parseArgument: result => result.Tokens.Count > 0 ? result.Tokens[0].Value.Split(',').ToArray() : []
        );

        rootCommand.AddArgument(path);
        rootCommand.AddOption(types);
        rootCommand.SetHandler(OnObfuscate, path, types);

        rootCommand.Invoke(args);
    }

    private static void OnObfuscate(string path, string[] types)
    {
        string file = Path.GetFullPath(path);
        string workingDirectory = Path.GetDirectoryName(file)!;

        Console.WriteLine($"Loading input file {file}");

        var module = ModuleDefinition.FromFile(file);
        var parameters = new ObfuscatorParameters
        {
            UseAnonymousTypes = true
        };

        var context = new ObfuscatorContext(module, parameters);

        ProcessModule(context, module, types);

        // Create output directory if it doesn't exist already.
        string outputDirectory = Path.Combine(workingDirectory, "Obfuscated");
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        // Ensure runtimeconfig.json file is copied over.
        string runtimeConfigFile = Path.ChangeExtension(file, ".runtimeconfig.json");;
        string runtimeConfigTarget = Path.Combine(outputDirectory, Path.GetFileName(runtimeConfigFile));
        if (File.Exists(runtimeConfigFile) && !File.Exists(runtimeConfigTarget))
            File.Copy(runtimeConfigFile, runtimeConfigTarget);

        // Save the final file.
        string outputFile = Path.Combine(outputDirectory, Path.GetFileName(file));
        Console.WriteLine($"Writing to {outputFile}...");
        module.Write(outputFile, new ManagedPEImageBuilder(new ConsoleErrorListener()));
    }

    private static void ProcessModule(ObfuscatorContext context, ModuleDefinition module, string[] includedTypes)
    {
        var selection = module.GetAllTypes()
            .Where(t => includedTypes.Length == 0 || includedTypes.Contains(t.FullName))
            .SelectMany(x => x.Methods)
            .Where(CanObfuscate)
            .ToArray();

        foreach (var method in selection)
            ProcessMethod(context, method);
    }

    private static bool CanObfuscate(MethodDefinition method)
    {
        if (method is not { CilMethodBody: {} body })
            return false;

        // Constructors cannot be made async by definition.
        if (method.IsConstructor)
            return false;

        // Async methods inherently do not support `ref` parameters.
        if (method.Parameters.Any(x => x.ParameterType is ByReferenceTypeSignature))
            return false;

        // We currently cannot transform exception handlers.
        if (body.ExceptionHandlers.Count > 0)
            return false;

        // We currently do not support "dup" yet.
        if (body.Instructions.Any(x => x.OpCode.Code == CilCode.Dup))
            return false;

        return true;
    }

    private static void ProcessMethod(ObfuscatorContext context, MethodDefinition method)
    {
        Console.WriteLine($"Processing {method}");

        try
        {
            var transformer = new MethodTransformer(context, method);
            transformer.ApplyTransformation();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!]: " + ex.Message);
        }
    }
}