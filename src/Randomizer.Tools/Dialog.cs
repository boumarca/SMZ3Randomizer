﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Randomizer.Tools
{

    static class Dialog
    {

        static readonly Regex command = new(@"^\{[^}]*\}");
        static readonly Regex invalid = new(@"(?<!^)\{[^}]*\}(?!$)", RegexOptions.Multiline);
        static readonly Regex character = new(@"(?<digit>[0-9])|(?<upper>[A-Z])|(?<lower>[a-z])");

        public static bool FitsSimple(string text)
        {
            const int maxBytes = 256;
            const int wrap = 19;

            var bytes = new List<byte>();
            var lines = text.Split('\n');
            var lineIndex = 0;
            foreach (var line in lines)
            {
                bytes.Add(lineIndex switch
                {
                    0 => 0x74, // row 1
                    1 => 0x75, // row 2
                    _ => 0x76, // row 3
                });
                var letters = line.Length > wrap ? line[..wrap] : line;
                foreach (var letter in letters)
                {
                    var write = LetterToBytes(letter);
                    if (write[0] == 0xFD)
                    {
                        bytes.AddRange(write);
                    }
                    else
                    {
                        foreach (var b in write)
                            bytes.AddRange(new byte[] { 0x00, b });
                    }
                }

                lineIndex += 1;

                if (lineIndex < lines.Length)
                {
                    if (lineIndex % 3 == 0)
                        bytes.Add(0x7E); // pause for input
                    if (lineIndex >= 3)
                        bytes.Add(0x73); // scroll
                }
            }

            return bytes.Count < maxBytes;
        }

        public static bool FitsCompiled(string text)
        {
            const int maxBytes = 2046;
            const int wrap = 19;

            if (invalid.IsMatch(text))
                throw new ArgumentException("Dialog commands must be placed on separate lines", nameof(text));

            bool padOut = false;
            bool pause = true;

            var bytes = new List<byte> { 0xFB };
            var lines = Wordwrap(text, wrap);
            var lineCount = lines.Count(l => !command.IsMatch(l));
            var lineIndex = 0;
            foreach (var line in lines)
            {
                var match = command.Match(line);
                if (match.Success)
                {
                    if (match.Value == "{NOTEXT}")
                        return true;

                    if (match.Value == "{INTRO}")
                    {
                        padOut = true;
                    }
                    if (match.Value == "{NOPAUSE}")
                    {
                        pause = false;
                        continue;
                    }

                    bytes.AddRange(CommandBytesFor(match.Value));

                    if (bytes.Count > maxBytes)
                        throw new ArgumentException("Command overflowed maximum byte length", nameof(text));

                    continue;
                }

                if (lineIndex > 0)
                {
                    bytes.Add(lineIndex switch
                    {
                        1 => 0xF8, // row 2
                        2 => 0xF9, // row 3
                        _ => 0xF6, // scroll
                    });
                }

                // The first box needs to fill the full width with spaces as the palette is loaded weird.
                var letters = padOut && lineIndex < 3 ? line.PadRight(wrap) : line;
                bytes.AddRange(letters.SelectMany(LetterToBytes));

                lineIndex += 1;

                if (pause && lineIndex % 3 == 0 && lineIndex < lineCount)
                    bytes.Add(0xFA); // pause for input
            }

            return bytes.Count < maxBytes;

            static byte[] CommandBytesFor(string text) => text switch
            {
                "{SPEED0}" => new byte[] { 0xFC, 0x00 },
                "{SPEED2}" => new byte[] { 0xFC, 0x02 },
                "{SPEED6}" => new byte[] { 0xFC, 0x06 },
                "{PAUSE1}" => new byte[] { 0xFE, 0x78, 0x01 },
                "{PAUSE3}" => new byte[] { 0xFE, 0x78, 0x03 },
                "{PAUSE5}" => new byte[] { 0xFE, 0x78, 0x05 },
                "{PAUSE7}" => new byte[] { 0xFE, 0x78, 0x07 },
                "{PAUSE9}" => new byte[] { 0xFE, 0x78, 0x09 },
                "{INPUT}" => new byte[] { 0xFA },
                "{CHOICE}" => new byte[] { 0xFE, 0x68 },
                "{ITEMSELECT}" => new byte[] { 0xFE, 0x69 },
                "{CHOICE2}" => new byte[] { 0xFE, 0x71 },
                "{CHOICE3}" => new byte[] { 0xFE, 0x72 },
                "{C:GREEN}" => new byte[] { 0xFE, 0x77, 0x07 },
                "{C:YELLOW}" => new byte[] { 0xFE, 0x77, 0x02 },
                "{HARP}" => new byte[] { 0xFE, 0x79, 0x2D },
                "{MENU}" => new byte[] { 0xFE, 0x6D, 0x00 },
                "{BOTTOM}" => new byte[] { 0xFE, 0x6D, 0x01 },
                "{NOBORDER}" => new byte[] { 0xFE, 0x6B, 0x02 },
                "{CHANGEPIC}" => new byte[] { 0xFE, 0x67, 0xFE, 0x67 },
                "{CHANGEMUSIC}" => new byte[] { 0xFE, 0x67 },
                "{INTRO}" => new byte[] { 0xFE, 0x6E, 0x00, 0xFE, 0x77, 0x07, 0xFC, 0x03, 0xFE, 0x6B, 0x02, 0xFE, 0x67 },
                "{IBOX}" => new byte[] { 0xFE, 0x6B, 0x02, 0xFE, 0x77, 0x07, 0xFC, 0x03, 0xF7 },
                var command => throw new ArgumentException($"Dialog text contained unknown command {command}", nameof(text)),
            };

        }

        static IEnumerable<string> Wordwrap(string text, int width)
        {
            return text.Split('\n').SelectMany(line =>
            {
                line = line.TrimEnd();
                if (line.Length <= width)
                    return new[] { line };
                var words = line.Split(' ');
                IEnumerable<string> lines = new[] { "" };
                lines = words.Aggregate(new Stack<string>(lines), (lines, word) =>
                {
                    var line = lines.Pop();
                    if (line.Length + word.Length <= width)
                    {
                        line = $"{line}{word} ";
                    }
                    else
                    {
                        if (line.Length > 0)
                            lines.Push(line);
                        line = word;
                        while (line.Length > width)
                        {
                            lines.Push(line[..width]);
                            line = line[width..];
                        }
                        line = $"{line} ";
                    }
                    lines.Push(line);
                    return lines;
                });
                return lines.Reverse().Select(l => l.TrimEnd());
            });
        }

        static byte[] LetterToBytes(char c)
        {
            var match = character.Match(c.ToString());
            return c switch
            {
                _ when match.Groups["digit"].Success => new byte[] { (byte)(c - '0' + 0xA0) },
                _ when match.Groups["upper"].Success => new byte[] { (byte)(c - 'A' + 0xAA) },
                _ when match.Groups["lower"].Success => new byte[] { (byte)(c - 'a' + 0x30) },
                _ => letters.TryGetValue(c, out var bytes) ? bytes : new byte[] { 0xFF },
            };
        }

        #region letter bytes lookup

        static readonly IDictionary<char, byte[]> letters = new Dictionary<char, byte[]> {
            { ' ', new byte[] { 0x4F } },
            { '?', new byte[] { 0xC6 } },
            { '!', new byte[] { 0xC7 } },
            { ',', new byte[] { 0xC8 } },
            { '-', new byte[] { 0xC9 } },
            { '…', new byte[] { 0xCC } },
            { '.', new byte[] { 0xCD } },
            { '~', new byte[] { 0xCE } },
            { '～', new byte[] { 0xCE } },
            { '\'', new byte[] { 0xD8 } },
            { '’', new byte[] { 0xD8 } },
            { '"', new byte[] { 0xD8 } },
            { ':', new byte[] { 0x4A } },
            { '@', new byte[] { 0x4B } },
            { '#', new byte[] { 0x4C } },
            { '¤', new byte[] { 0x4D, 0x4E } }, // Morphing ball
            { '_', new byte[] { 0xFF } }, // Full width space
            { '£', new byte[] { 0xFE, 0x6A } }, // link's name compressed
            { '>', new byte[] { 0xD2, 0xD3 } }, // link face
            { '%', new byte[] { 0xDD } }, // Hylian Bird
            { '^', new byte[] { 0xDE } }, // Hylian Ankh
            { '=', new byte[] { 0xDF } }, // Hylian Wavy lines
            { '↑', new byte[] { 0xE0 } },
            { '↓', new byte[] { 0xE1 } },
            { '→', new byte[] { 0xE2 } },
            { '←', new byte[] { 0xE3 } },
            { '≥', new byte[] { 0xE4 } }, // cursor
            { '¼', new byte[] { 0xE5, 0xE7 } }, // 1/4 heart
            { '½', new byte[] { 0xE6, 0xE7 } }, // 1/2 heart
            { '¾', new byte[] { 0xE8, 0xE9 } }, // 3/4 heart
            { '♥', new byte[] { 0xEA, 0xEB } }, // full heart
            { 'ᚋ', new byte[] { 0xFE, 0x6C, 0x00 } }, // var 0
            { 'ᚌ', new byte[] { 0xFE, 0x6C, 0x01 } }, // var 1
            { 'ᚍ', new byte[] { 0xFE, 0x6C, 0x02 } }, // var 2
            { 'ᚎ', new byte[] { 0xFE, 0x6C, 0x03 } }, // var 3

            { 'あ', new byte[] { 0x00 } },
            { 'い', new byte[] { 0x01 } },
            { 'う', new byte[] { 0x02 } },
            { 'え', new byte[] { 0x03 } },
            { 'お', new byte[] { 0x04 } },
            { 'や', new byte[] { 0x05 } },
            { 'ゆ', new byte[] { 0x06 } },
            { 'よ', new byte[] { 0x07 } },
            { 'か', new byte[] { 0x08 } },
            { 'き', new byte[] { 0x09 } },
            { 'く', new byte[] { 0x0A } },
            { 'け', new byte[] { 0x0B } },
            { 'こ', new byte[] { 0x0C } },
            { 'わ', new byte[] { 0x0D } },
            { 'を', new byte[] { 0x0E } },
            { 'ん', new byte[] { 0x0F } },
            { 'さ', new byte[] { 0x10 } },
            { 'し', new byte[] { 0x11 } },
            { 'す', new byte[] { 0x12 } },
            { 'せ', new byte[] { 0x13 } },
            { 'そ', new byte[] { 0x14 } },
            { 'が', new byte[] { 0x15 } },
            { 'ぎ', new byte[] { 0x16 } },
            { 'ぐ', new byte[] { 0x17 } },
            { 'た', new byte[] { 0x18 } },
            { 'ち', new byte[] { 0x19 } },
            { 'つ', new byte[] { 0x1A } },
            { 'て', new byte[] { 0x1B } },
            { 'と', new byte[] { 0x1C } },
            { 'げ', new byte[] { 0x1D } },
            { 'ご', new byte[] { 0x1E } },
            { 'ざ', new byte[] { 0x1F } },
            { 'な', new byte[] { 0x20 } },
            { 'に', new byte[] { 0x21 } },
            { 'ぬ', new byte[] { 0x22 } },
            { 'ね', new byte[] { 0x23 } },
            { 'の', new byte[] { 0x24 } },
            { 'じ', new byte[] { 0x25 } },
            { 'ず', new byte[] { 0x26 } },
            { 'ぜ', new byte[] { 0x27 } },
            { 'は', new byte[] { 0x28 } },
            { 'ひ', new byte[] { 0x29 } },
            { 'ふ', new byte[] { 0x2A } },
            { 'へ', new byte[] { 0x2B } },
            { 'ほ', new byte[] { 0x2C } },
            { 'ぞ', new byte[] { 0x2D } },
            { 'だ', new byte[] { 0x2E } },
            { 'ぢ', new byte[] { 0x2F } },

            { 'ア', new byte[] { 0x50 } },
            { 'イ', new byte[] { 0x51 } },
            { 'ウ', new byte[] { 0x52 } },
            { 'エ', new byte[] { 0x53 } },
            { 'オ', new byte[] { 0x54 } },
            { 'ヤ', new byte[] { 0x55 } },
            { 'ユ', new byte[] { 0x56 } },
            { 'ヨ', new byte[] { 0x57 } },
            { 'カ', new byte[] { 0x58 } },
            { 'キ', new byte[] { 0x59 } },
            { 'ク', new byte[] { 0x5A } },
            { 'ケ', new byte[] { 0x5B } },
            { 'コ', new byte[] { 0x5C } },
            { 'ワ', new byte[] { 0x5D } },
            { 'ヲ', new byte[] { 0x5E } },
            { 'ン', new byte[] { 0x5F } },
            { 'サ', new byte[] { 0x60 } },
            { 'シ', new byte[] { 0x61 } },
            { 'ス', new byte[] { 0x62 } },
            { 'セ', new byte[] { 0x63 } },
            { 'ソ', new byte[] { 0x64 } },
            { 'ガ', new byte[] { 0x65 } },
            { 'ギ', new byte[] { 0x66 } },
            { 'グ', new byte[] { 0x67 } },
            { 'タ', new byte[] { 0x68 } },
            { 'チ', new byte[] { 0x69 } },
            { 'ツ', new byte[] { 0x6A } },
            { 'テ', new byte[] { 0x6B } },
            { 'ト', new byte[] { 0x6C } },
            { 'ゲ', new byte[] { 0x6D } },
            { 'ゴ', new byte[] { 0x6E } },
            { 'ザ', new byte[] { 0x6F } },
            { 'ナ', new byte[] { 0x70 } },
            { 'ニ', new byte[] { 0x71 } },
            { 'ヌ', new byte[] { 0x72 } },
            { 'ネ', new byte[] { 0x73 } },
            { 'ノ', new byte[] { 0x74 } },
            { 'ジ', new byte[] { 0x75 } },
            { 'ズ', new byte[] { 0x76 } },
            { 'ゼ', new byte[] { 0x77 } },
            { 'ハ', new byte[] { 0x78 } },
            { 'ヒ', new byte[] { 0x79 } },
            { 'フ', new byte[] { 0x7A } },
            { 'ヘ', new byte[] { 0x7B } },
            { 'ホ', new byte[] { 0x7C } },
            { 'ゾ', new byte[] { 0x7D } },
            { 'ダ', new byte[] { 0x7E } },
            { 'マ', new byte[] { 0x80 } },
            { 'ミ', new byte[] { 0x81 } },
            { 'ム', new byte[] { 0x82 } },
            { 'メ', new byte[] { 0x83 } },
            { 'モ', new byte[] { 0x84 } },
            { 'ヅ', new byte[] { 0x85 } },
            { 'デ', new byte[] { 0x86 } },
            { 'ド', new byte[] { 0x87 } },
            { 'ラ', new byte[] { 0x88 } },
            { 'リ', new byte[] { 0x89 } },
            { 'ル', new byte[] { 0x8A } },
            { 'レ', new byte[] { 0x8B } },
            { 'ロ', new byte[] { 0x8C } },
            { 'バ', new byte[] { 0x8D } },
            { 'ビ', new byte[] { 0x8E } },
            { 'ブ', new byte[] { 0x8F } },
            { 'ベ', new byte[] { 0x90 } },
            { 'ボ', new byte[] { 0x91 } },
            { 'パ', new byte[] { 0x92 } },
            { 'ピ', new byte[] { 0x93 } },
            { 'プ', new byte[] { 0x94 } },
            { 'ペ', new byte[] { 0x95 } },
            { 'ポ', new byte[] { 0x96 } },
            { 'ャ', new byte[] { 0x97 } },
            { 'ュ', new byte[] { 0x98 } },
            { 'ョ', new byte[] { 0x99 } },
            { 'ッ', new byte[] { 0x9A } },
            { 'ァ', new byte[] { 0x9B } },
            { 'ィ', new byte[] { 0x9C } },
            { 'ゥ', new byte[] { 0x9D } },
            { 'ェ', new byte[] { 0x9E } },
            { 'ォ', new byte[] { 0x9F } },
        };

        #endregion

    }

}
