
using System;
using System.Collections.Generic;
using System.Text;

namespace MiniOS
{
    // ultra-minimal C->BF compiler: only int main(){ puts("..."); putchar(<literal>); }
    public static class MiniCCompiler
    {
        public static string Compile(string src)
        {
            var s = new Scanner(src);
            s.Expect("int"); s.Ident("main"); s.Expect("("); s.Expect(")"); s.Expect("{");
            var bf = new StringBuilder();
            while (!s.Try("}"))
            {
                if (s.TryIdent("puts"))
                {
                    s.Expect("(");
                    var str = s.StringLiteral();
                    s.Expect(")"); s.Expect(";");
                    foreach (var ch in str)
                    {
                        bf.Append("[-]");
                        bf.Append(new string('+', (byte)ch));
                        bf.Append(".");
                    }
                    bf.Append("[-]");
                }
                else if (s.TryIdent("putchar"))
                {
                    s.Expect("(");
                    var c = s.CharOrInt();
                    s.Expect(")"); s.Expect(";");
                    bf.Append("[-]");
                    bf.Append(new string('+', c & 0xFF));
                    bf.Append(".");
                    bf.Append("[-]");
                }
                else if (s.TryIdent("return"))
                {
                    // ignore return value
                    while (!s.Try(";")) s.Next();
                }
                else
                {
                    throw new Exception("Only puts/putchar/return supported in MiniC v1");
                }
            }
            return bf.ToString();
        }

        private class Scanner
        {
            private readonly string _t; int _i = 0;
            public Scanner(string t) { _t = t; }
            public void Skip()
            {
                while (_i < _t.Length)
                {
                    if (char.IsWhiteSpace(_t[_i])) { _i++; continue; }
                    if (_i + 1 < _t.Length && _t[_i] == '/' && _t[_i + 1] == '/') { while (_i < _t.Length && _t[_i] != '\n') _i++; continue; }
                    if (_i + 1 < _t.Length && _t[_i] == '/' && _t[_i + 1] == '*') { _i += 2; while (_i + 1 < _t.Length && !(_t[_i] == '*' && _t[_i + 1] == '/')) _i++; _i += 2; continue; }
                    break;
                }
            }
            public void Expect(string s) { Skip(); if (!Try(s)) throw new Exception($"expected '{s}'"); }
            public bool Try(string s) { Skip(); if (_i + s.Length > _t.Length) return false; if (_t.Substring(_i, s.Length) == s) { _i += s.Length; return true; } return false; }
            public bool TryIdent(string id) { Skip(); int j = _i; var got = Ident(); if (got == id) return true; _i = j; return false; }
            public string Ident(string? must = null)
            {
                Skip(); int j = _i;
                if (j >= _t.Length || !(char.IsLetter(_t[j]) || _t[j] == '_')) throw new Exception("ident expected");
                j++;
                while (j < _t.Length && (char.IsLetterOrDigit(_t[j]) || _t[j] == '_')) j++;
                var id = _t.Substring(_i, j - _i); _i = j;
                if (must != null && id != must) throw new Exception($"expected ident {must}");
                return id;
            }
            public string StringLiteral()
            {
                Skip(); if (_t[_i] != '"') throw new Exception("string expected"); _i++;
                var sb = new StringBuilder();
                while (_i < _t.Length && _t[_i] != '"')
                {
                    char c = _t[_i++];
                    if (c == '\\' && _i < _t.Length)
                    {
                        char n = _t[_i++];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => n });
                    }
                    else sb.Append(c);
                }
                if (_i >= _t.Length) throw new Exception("unterminated string");
                _i++;
                return sb.ToString();
            }
            public int CharOrInt()
            {
                Skip();
                if (_t[_i] == '\'') { _i++; int vv = _t[_i++]; if (_t[_i] != '\'') throw new Exception("char literal error"); _i++; return vv; }
                int v = 0; bool any = false;
                while (_i < _t.Length && char.IsDigit(_t[_i])) { any = true; v = v * 10 + (_t[_i] - '0'); _i++; }
                if (!any) throw new Exception("int expected");
                return v;
            }
            public void Next() { if (_i < _t.Length) _i++; }
        }
    }
}
