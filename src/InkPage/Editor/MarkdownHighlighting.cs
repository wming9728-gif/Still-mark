using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace InkPage.Editor;

/// <summary>Source-view syntax colouring for Markdown. Purely visual: it never changes the text.</summary>
public static class MarkdownHighlighting
{
    private sealed record Palette(string Fg, string Heading, string Accent, string Code, string FenceFg, string FenceBg,
        string Mute, string Html, string Comment, string InlineBg);

    private static readonly Palette LightP = new("#2B2F36", "#1F2328", "#2F6FDE", "#953800", "#24292F", "#F3F5F7",
        "#6B7280", "#116329", "#8A919C", "#F1F3F5");
    private static readonly Palette DarkP = new("#D9DDE3", "#F0F3F6", "#6EA4FF", "#FFA657", "#E6EDF3", "#23272E",
        "#959CA6", "#7EE787", "#7F8792", "#262B32");

    private static IHighlightingDefinition? _light, _dark;

    public static IHighlightingDefinition? Get(bool dark)
    {
        try
        {
            return dark ? _dark ??= Build(DarkP) : _light ??= Build(LightP);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex); // colouring is optional; never block editing
            return null;
        }
    }

    private static IHighlightingDefinition Build(Palette p)
    {
        var xml = $$"""
<SyntaxDefinition name="Markdown-InkPage" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Heading" foreground="{{p.Heading}}" fontWeight="bold" />
  <Color name="HeadingMark" foreground="{{p.Accent}}" fontWeight="bold" />
  <Color name="Fence" foreground="{{p.FenceFg}}" />
  <Color name="Code" foreground="{{p.Code}}" background="{{p.InlineBg}}" />
  <Color name="Strong" fontWeight="bold" />
  <Color name="Emphasis" fontStyle="italic" />
  <Color name="Strike" foreground="{{p.Mute}}" strikethrough="true" />
  <Color name="Link" foreground="{{p.Accent}}" />
  <Color name="Quote" foreground="{{p.Mute}}" />
  <Color name="ListMarker" foreground="{{p.Accent}}" fontWeight="bold" />
  <Color name="Punct" foreground="{{p.Mute}}" />
  <Color name="Html" foreground="{{p.Html}}" />
  <Color name="Comment" foreground="{{p.Comment}}" fontStyle="italic" />
  <RuleSet ignoreCase="false">
    <Span color="Fence" multiline="true">
      <Begin>^[ ]{0,3}(```|~~~)</Begin>
      <End>^[ ]{0,3}(```|~~~)[ \t]*$</End>
    </Span>
    <Span color="Comment" multiline="true">
      <Begin>&lt;!--</Begin>
      <End>--&gt;</End>
    </Span>
    <Span color="Heading">
      <Begin color="HeadingMark">^[ ]{0,3}\#{1,6}(?=[ \t]|$)</Begin>
    </Span>
    <Span color="Quote">
      <Begin color="ListMarker">^[ ]{0,3}&gt;</Begin>
    </Span>
    <Rule color="Punct">^[ ]{0,3}(?&lt;c&gt;[-*_])([ \t]*\k&lt;c&gt;){2,}[ \t]*$</Rule>
    <Rule color="Punct">^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$</Rule>
    <Rule color="ListMarker">^[ \t]*([-*+]|\d{1,9}[.)])(?=[ \t])([ \t]+\[[ xX]\](?=[ \t]))?</Rule>
    <Rule color="Code">(?&lt;q&gt;``?)[^`\r\n]+?\k&lt;q&gt;</Rule>
    <Rule color="Strong">(?&lt;s&gt;\*\*|__)(?=\S)[^\r\n]+?(?&lt;=\S)\k&lt;s&gt;</Rule>
    <Rule color="Strike">~~(?=\S)[^\r\n]+?(?&lt;=\S)~~</Rule>
    <Rule color="Emphasis">(?&lt;![*\w\\])\*(?=[^\s*])[^*\r\n]+?(?&lt;=[^\s*])\*(?!\*)</Rule>
    <Rule color="Emphasis">(?&lt;![_\w\\])_(?=[^\s_])[^_\r\n]+?(?&lt;=[^\s_])_(?![_\w])</Rule>
    <Rule color="Link">!?\[[^\]\r\n]*\]\([^)\r\n]*\)</Rule>
    <Rule color="Link">!?\[[^\]\r\n]+\]\[[^\]\r\n]*\]</Rule>
    <Rule color="Link">^[ ]{0,3}\[[^\]\r\n]+\]:[ \t]+\S+.*$</Rule>
    <Rule color="Link">&lt;(https?|ftp|mailto):[^&gt;\s]+&gt;</Rule>
    <Rule color="Link">\b(https?|ftp)://[^\s&lt;&gt;()\]]+</Rule>
    <Rule color="Html">&lt;/?[A-Za-z][A-Za-z0-9-]*(\s[^&lt;&gt;\r\n]*)?/?&gt;</Rule>
    <Rule color="Punct">\\[\\`*_{}\[\]()\#+\-.!|~&lt;&gt;]</Rule>
    <Rule color="Punct">\|</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        using var reader = XmlReader.Create(new StringReader(xml));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
