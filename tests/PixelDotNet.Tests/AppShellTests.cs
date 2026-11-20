using Donkeh.Testing;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

public sealed class AppShellTests
{
    [Fact]
    public void ApplicationRootRendersTheWholeShell()
    {
        var page = ComponentTest.Render(C<App>());

        Assert.NotNull(page.TryFind(".app"));
        Assert.NotNull(page.TryFind(".menubar"));
        Assert.NotNull(page.TryFind(".rail"));
        Assert.NotNull(page.TryFind(".optionsbar"));
        Assert.NotNull(page.TryFind(".viewport"));
        Assert.NotNull(page.TryFind(".side"));
        Assert.NotNull(page.TryFind(".statusbar"));
        Assert.NotNull(page.TryFind(".paint-canvas"));
        Assert.Single(page.FindAll(".doctab"));
        Assert.Contains("Moonlight", page.Find(".doctab-name").Text);
        Assert.Null(page.TryFind(".font-popup"));
        Assert.Null(page.TryFind(".help-panel"));
    }

    [Fact]
    public void ToolRailAndShortcutsUpdateTheOptionsBar()
    {
        var page = ComponentTest.Render(C<App>());

        page.Click("[title^='Eraser']");
        Assert.Equal("Eraser", page.Find(".tool-badge-name").Text);

        page.KeyDown(".app", "M");
        Assert.Equal("Marquee", page.Find(".tool-badge-name").Text);

        page.KeyDown(".app", "B");
        Assert.Equal("Brush", page.Find(".tool-badge-name").Text);

        page.KeyDown(".app", "X");
        page.KeyDown(".app", "D");
        Assert.Equal("100%", page.Find(".status-zoom-reset").Text);
    }

    [Fact]
    public void FileMenuCreatesANewDocumentTab()
    {
        var page = ComponentTest.Render(C<App>());

        page.FindAll(".menu-title").First(title => title.Text == "File").Click();
        Assert.NotEmpty(page.FindAll(".menu-item"));

        page.FindByText("New Small Document").Click();

        Assert.Equal(2, page.FindAll(".doctab").Count);
        Assert.Contains(page.FindAll(".status-item"), item => item.Text.Contains("480"));
    }

    [Fact]
    public void QuickSaveOpensTheBrowserAndEscapeClosesIt()
    {
        var page = ComponentTest.Render(C<App>());

        page.Click("[title='Choose where to save the image']");
        Assert.NotNull(page.TryFind(".file-panel"));

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".file-panel"));
    }

    [Fact]
    public void TextPopupsAppearAndDismiss()
    {
        var page = ComponentTest.Render(C<App>());

        page.KeyDown(".app", "T");
        page.Click("[title='Choose a font']");
        Assert.NotNull(page.TryFind(".font-popup"));

        page.Find(".font-popup-add").Click();
        Assert.NotNull(page.TryFind(".font-url"));
        Assert.Null(page.TryFind(".font-popup"));

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".font-url"));

        page.KeyDown(".app", "T");
        page.FindByText("FX").Click();
        Assert.NotNull(page.TryFind(".text-fx-popup"));

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".text-fx-popup"));
    }

    [Fact]
    public void HoveringAToolShowsTheFloatingNameTip()
    {
        var page = ComponentTest.Render(C<App>());

        page.Hover("[title^='Eraser']");

        Assert.NotNull(page.TryFind(".tip-float"));
        Assert.Equal("Eraser", page.Find(".tip-title").Text);
    }

    [Fact]
    public void HelpChevronOpensTheModal()
    {
        var page = ComponentTest.Render(C<App>());
        page.FindAll(".menu-title").First(title => title.Text == "Help").Click();
        page.FindByText("Enable Tooltips").Click();

        page.Hover("[title^='Eraser']");
        page.Find(".tip-more").Click();

        Assert.NotNull(page.TryFind(".help-panel"));
        Assert.Equal("Eraser", page.Find(".help-title").Text);

        page.FindByText("Got it").Click();
        Assert.Null(page.TryFind(".help-panel"));
    }

    [Fact]
    public void UndoButtonTracksTheHistoryAndUndoesTheAction()
    {
        var page = ComponentTest.Render(C<App>());
        int layers = page.FindAll(".layer").Count;
        page.KeyDown(".app", "Ctrl+Shift+N");
        Assert.Equal(layers + 1, page.FindAll(".layer").Count);

        page.Click("[title='Undo the last step (Ctrl+Z)']");
        Assert.Equal(layers, page.FindAll(".layer").Count);
    }
}
