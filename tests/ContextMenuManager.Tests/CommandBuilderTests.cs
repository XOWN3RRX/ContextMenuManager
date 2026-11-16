using ContextMenuManager.Core;

namespace ContextMenuManager.Tests;

public class CommandBuilderTests
{
    [Fact]
    public void Background_uses_percent_V_for_the_path()
    {
        var entry = new MenuEntry { Program = @"C:\Program Files\Git\git-bash.exe", Arguments = "\"--cd={path}\"" };

        string command = CommandBuilder.BuildRegistryCommand(entry, MenuLocations.Background);

        Assert.Equal("\"C:\\Program Files\\Git\\git-bash.exe\" \"--cd=%V\"", command);
    }

    [Theory]
    [InlineData(MenuLocations.Folders)]
    [InlineData(MenuLocations.Files)]
    [InlineData(MenuLocations.Drives)]
    public void Items_use_percent_1_for_the_path(MenuLocations location)
    {
        var entry = new MenuEntry { Program = "notepad.exe", Arguments = "\"{PATH}\"" };

        Assert.Equal("notepad.exe \"%1\"", CommandBuilder.BuildRegistryCommand(entry, location));
    }

    [Fact]
    public void Program_with_environment_variable_is_quoted()
    {
        var entry = new MenuEntry { Program = @"%SystemRoot%\System32\cmd.exe" };

        Assert.Equal("\"%SystemRoot%\\System32\\cmd.exe\"", CommandBuilder.BuildRegistryCommand(entry, MenuLocations.Background));
    }

    [Fact]
    public void Already_quoted_program_is_left_alone()
    {
        var entry = new MenuEntry { Program = "\"C:\\My Tools\\tool.exe\"", Arguments = "/x" };

        Assert.Equal("\"C:\\My Tools\\tool.exe\" /x", CommandBuilder.BuildRegistryCommand(entry, MenuLocations.Files));
    }

    [Fact]
    public void Preview_fills_in_the_clicked_path_and_keeps_dollar_signs()
    {
        var entry = new MenuEntry { Program = "code.exe", Arguments = "\"{path}\"" };

        string command = CommandBuilder.BuildPreviewCommand(entry, MenuLocations.Folders, @"C:\Work\$money");

        Assert.Equal("code.exe \"C:\\Work\\$money\"", command);
    }

    [Fact]
    public void Preview_does_not_replace_environment_variables_that_start_with_V()
    {
        var entry = new MenuEntry { Program = "%VSCODE%\\code.exe", Arguments = "%V" };

        string command = CommandBuilder.BuildPreviewCommand(entry, MenuLocations.Background, @"C:\Desk");

        Assert.Equal("\"%VSCODE%\\code.exe\" C:\\Desk", command);
    }

    [Theory]
    [InlineData("\"C:\\a b\\c.exe\" -x \"y\"", "C:\\a b\\c.exe", "-x \"y\"")]
    [InlineData("cmd.exe /c echo hi", "cmd.exe", "/c echo hi")]
    [InlineData("notepad.exe", "notepad.exe", "")]
    [InlineData("", "", "")]
    public void Split_separates_program_and_arguments(string commandLine, string fileName, string arguments)
    {
        Assert.Equal((fileName, arguments), CommandBuilder.Split(commandLine));
    }
}
