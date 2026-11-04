using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's offer of a spelling dictionary: the question, what will be fetched from where and saved where (the
/// cells' consent sentences, <see cref="DictionaryOfferModel.Lines"/>), and Download / Not now. Download ends in the
/// client's <c>InstallDictionary</c>, the method the cells' ⏎ ends in; nothing is fetched before it.
/// </summary>
internal sealed class DictionaryOfferView : DockPanel, IModalView
{
    private readonly DictionaryOfferSession _session;
    private readonly Action _done;
    private readonly Button _download;
    private readonly Button _later;

    public DictionaryOfferView(DictionaryOfferSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _done = done;
        var model = session.Model;

        var heading = ModalParts.Heading(model.Question, colours);
        heading.Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space16, NativeTokens.Space24, NativeTokens.Space8);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var facts = new StackPanel { Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space2, NativeTokens.Space24, NativeTokens.Space4), Spacing = NativeTokens.Space6 };
        foreach (var line in model.Lines)
        {
            facts.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(line),
                Foreground = colours.Brush(line.Length == 0 ? colours.Dim : colours.Text),
                TextWrapping = TextWrapping.Wrap,
                FontSize = NativeTokens.Body,
            });
        }

        SetDock(facts, Dock.Top);
        Children.Add(facts);

        var footer = ModalParts.Footer(DictionaryOfferModel.Keys, colours);
        SetDock(footer, Dock.Bottom);

        _download = ModalParts.Button("Download", "Enter", colours, Accept, accent: true);
        _download.IsEnabled = model.Folder is not null && !model.Installed;
        _later = ModalParts.Button("Not now", "Esc", colours, Decline);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = NativeTokens.Space10,
            Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space14, NativeTokens.Space24, 0),
        };
        buttons.Children.Add(_download);
        buttons.Children.Add(_later);
        SetDock(buttons, Dock.Bottom);
        Children.Add(footer);
        Children.Add(buttons);
        Children.Add(new Panel());

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    public string Title => "Spelling dictionary";

    public double PreferredWidth => 560;

    public bool Centred => true;

    /// <summary>The two buttons, for a test.</summary>
    public Button DownloadButton => _download;

    public Button LaterButton => _later;

    public void FocusFirst() => (_download.IsEnabled ? _download : _later).Focus();

    public void Detach()
    {
    }

    /// <summary>Download: the session ends and the client fetches and installs.</summary>
    public void Accept()
    {
        _session.Accept();
        _done();
    }

    /// <summary>Not now: nothing is fetched.</summary>
    public void Decline()
    {
        _session.Dismiss();
        _done();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Decline();
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            e.Handled = true;
            Accept();
        }
    }
}
