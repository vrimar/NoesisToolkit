namespace NoesisToolkit.Tests;

public class XamlCompilerOptionsTests
{
    [Test]
    [Arguments(".xaml", "/a/b/Theme.xaml", true)]
    [Arguments(".xaml", "/a/b/Theme.nxaml", false)]
    [Arguments(".nxaml", "/a/b/Theme.nxaml", true)]
    [Arguments(".xaml;.nxaml", "/a/b/Theme.nxaml", true)]
    [Arguments(".xaml,.nxaml", "/a/b/Theme.nxaml", true)]
    [Arguments("xaml", "/a/b/Theme.xaml", true)]
    [Arguments(".xaml", "/a/b/notes.txt", false)]
    public async Task Matches_honours_the_configured_extensions(
        string extensions,
        string path,
        bool expected
    )
    {
        await Assert
            .That(TestOptions.With(extensions: extensions).Matches(path))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task Extensions_default_to_xaml()
    {
        await Assert.That(TestOptions.With().Matches("/a/Theme.xaml")).IsTrue();
        await Assert.That(TestOptions.With().Matches("/a/Theme.nxaml")).IsFalse();
    }
}
