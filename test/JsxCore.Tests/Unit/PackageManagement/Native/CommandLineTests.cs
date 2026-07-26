using JsxCore.Tool.Cli;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

public class CommandLineTests
{
    [Fact]
    public void Parse_VersionAsASeparateArgument_ReadsIt() =>
        CommandLine.Parse(["add", "marked", "--version", "^12"]).Value("version").ShouldBe("^12");

    [Fact]
    public void Parse_VersionJoinedByEquals_ReadsIt() =>
        CommandLine.Parse(["add", "marked", "--version=^12"]).Value("version").ShouldBe("^12");

    [Fact]
    public void Parse_ShortFormVersion_ReadsIt() =>
        CommandLine.Parse(["add", "marked", "-v", "^12"]).Value("version", "v").ShouldBe("^12");

    [Fact]
    public void Parse_PackageNames_AreKeptInOrderAfterTheVerb()
    {
        var command = CommandLine.Parse(["add", "marked", "classnames", "--dev"]);

        command.Positional.ShouldBe(["add", "marked", "classnames"]);
    }

    [Fact]
    public void Parse_FlagFollowedByAPackageName_DoesNotSwallowIt()
    {
        // --dev takes no value, so the name after it is a package rather than its argument.
        var command = CommandLine.Parse(["add", "--dev", "typescript"]);

        command.Has("dev").ShouldBeTrue();
        command.Positional.ShouldBe(["add", "typescript"]);
    }

    [Fact]
    public void Has_AnyOfTheAliases_IsTrue()
    {
        CommandLine.Parse(["add", "x", "-D"]).Has("dev", "save-dev", "D").ShouldBeTrue();
        CommandLine.Parse(["add", "x", "--save-dev"]).Has("dev", "save-dev", "D").ShouldBeTrue();
        CommandLine.Parse(["add", "x"]).Has("dev", "save-dev", "D").ShouldBeFalse();
    }
}
