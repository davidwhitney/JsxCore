using JsxCore.Tool;

// Commands are deliberately thin: each parses its arguments and hands off to a class that can be
// tested without a process. Failure is reported on stderr with a non-zero exit code, which the
// targets surface as a build warning rather than swallowing.
//
// Every verb here is invoked by the MSBuild targets. The ones a person types, as
// "dotnet npm add marked", are JsxCore.Npm: it ships as its own tool and needs none of the view
// engine to do its job.
try
{
    return args switch
    {
        ["analyse", .. var rest] => AnalyseCommand.Run(Arguments.Parse(rest)),
        ["tsconfig", .. var rest] => TsConfigCommand.Run(Arguments.Parse(rest)),
        ["provision", .. var rest] => ProvisionCommand.Run(Arguments.Parse(rest)),
        ["types", .. var rest] => TypesCommand.Run(Arguments.Parse(rest)),
        ["minify", .. var rest] => MinifyCommand.Run(Arguments.Parse(rest)),
        ["assets", .. var rest] => AssetsCommand.Run(Arguments.Parse(rest)),

        _ => Fail($"JsxCore.Tool has no command '{string.Join(' ', args)}'.")
    };
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
