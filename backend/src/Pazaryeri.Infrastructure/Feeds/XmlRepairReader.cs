using System.Text;

namespace Pazaryeri.Infrastructure.Feeds;

/// <summary>
/// Tedarikçi XML'lerinde sık görülen küçük bozuklukları akış olarak onarır, böylece dosya tümden reddedilmez:
/// kök öğeden önceki metin ("This XML file does not appear…" gibi tarayıcıdan kopyalanmış satırlar) atılır,
/// kaçışsız <c>&amp;</c> ("Health &amp; Beauty", URL'lerde <c>?a=1&amp;b=2</c>) ve kaçışsız <c>&lt;</c> kaçışlanır,
/// XML'in tanımadığı adlı varlıklar (<c>&amp;nbsp;</c>) düz metin kalır, yorumlar atılır. CDATA bölümlerine dokunulmaz.
/// Hiçbir varlık genişletilmez; güvenlik ayarları (DTD yok sayma, dış varlık yok) aynen geçerlidir.
/// </summary>
internal sealed class XmlRepairReader(TextReader source) : TextReader
{
    private enum Mode { Junk, Text, CData }

    private char[] _buf = new char[16384];
    private int _pos, _len;
    private bool _eof;
    private Mode _mode = Mode.Junk;
    private string _pending = "";
    private int _pendingPos;

    /// <summary>Geçerli konumdan k karakter ilerideki karakter; yoksa -1.</summary>
    private int At(int k)
    {
        while (_pos + k >= _len)
        {
            if (_eof) return -1;
            if (_pos > 0) { Array.Copy(_buf, _pos, _buf, 0, _len - _pos); _len -= _pos; _pos = 0; }
            if (_len == _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
            var n = source.Read(_buf, _len, _buf.Length - _len);
            if (n == 0) { _eof = true; return -1; }
            _len += n;
        }
        return _buf[_pos + k];
    }

    private bool StartsWith(string s)
    {
        for (var i = 0; i < s.Length; i++) if (At(i) != s[i]) return false;
        return true;
    }

    private static bool IsMarkupStart(int c, bool allowClose) =>
        c >= 0 && (char.IsLetter((char)c) || c is '_' or ':' or '?' or '!' || (allowClose && c == '/'));

    /// <summary>'&amp;' karakterinden sonra XML'in tanıdığı bir karakter/varlık başvurusu mu geliyor (&amp;amp; &amp;#38; &amp;#x26;)?</summary>
    private bool IsValidReference()
    {
        var name = new StringBuilder();
        for (var k = 1; k <= 10; k++)
        {
            var c = At(k);
            if (c == ';') return IsXmlReference(name.ToString());
            if (c < 0 || !(char.IsAsciiLetterOrDigit((char)c) || c == '#')) return false;
            name.Append((char)c);
        }
        return false;
    }

    private static bool IsXmlReference(string name)
    {
        if (name is "amp" or "lt" or "gt" or "quot" or "apos") return true;
        if (name.Length >= 2 && name[0] == '#')
        {
            var hex = name[1] is 'x' or 'X';
            var digits = name.AsSpan(hex ? 2 : 1);
            return digits.Length is >= 1 and <= 7 && (hex ? digits.ToString().All(char.IsAsciiHexDigit) : digits.ToString().All(char.IsAsciiDigit));
        }
        return false;
    }

    private void SkipComment()
    {
        _pos += 4;
        while (At(0) >= 0 && !StartsWith("-->")) _pos++;
        if (At(0) >= 0) _pos += 3;
    }

    private bool Fill()
    {
        var sb = new StringBuilder();
        while (sb.Length < 4096)
        {
            var c = At(0);
            if (c < 0) break;
            switch (_mode)
            {
                case Mode.Junk:
                    if (c == '<' && IsMarkupStart(At(1), false)) _mode = Mode.Text; else _pos++;
                    break;
                case Mode.CData:
                    if (c == ']' && At(1) == ']' && At(2) == '>') { sb.Append("]]>"); _pos += 3; _mode = Mode.Text; }
                    else { sb.Append((char)c); _pos++; }
                    break;
                default:
                    if (c == '<')
                    {
                        if (StartsWith("<!--")) SkipComment();
                        else if (StartsWith("<![CDATA[")) { sb.Append("<![CDATA["); _pos += 9; _mode = Mode.CData; }
                        else if (IsMarkupStart(At(1), true)) { sb.Append('<'); _pos++; }
                        else { sb.Append("&lt;"); _pos++; }
                    }
                    else if (c == '&') { sb.Append(IsValidReference() ? "&" : "&amp;"); _pos++; }
                    else { sb.Append((char)c); _pos++; }
                    break;
            }
        }
        _pending = sb.ToString();
        _pendingPos = 0;
        return _pending.Length > 0;
    }

    public override int Peek() => _pendingPos < _pending.Length || Fill() ? _pending[_pendingPos] : -1;

    public override int Read() => _pendingPos < _pending.Length || Fill() ? _pending[_pendingPos++] : -1;

    public override int Read(char[] buffer, int index, int count)
    {
        if (count == 0) return 0;
        if (_pendingPos >= _pending.Length && !Fill()) return 0;
        var n = Math.Min(count, _pending.Length - _pendingPos);
        _pending.CopyTo(_pendingPos, buffer, index, n);
        _pendingPos += n;
        return n;
    }
}
