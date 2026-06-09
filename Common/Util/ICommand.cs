using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;

namespace March7thHoney.Util
{
    public static class IConsole
    {
        #region Constants & Colors

        public const string PrefixContent = "March7thHoney > ";

        private const string RedColor = "\e[38;2;255;0;0m";
        private const string PinkColor = "\e[38;2;235;111;146m";
        private const string ResetColor = "\e[0m";

        private static readonly string[] BannerPalette =
        {
            "#54C3F7","#5ABEF2","#60BAEE","#67B5EA","#6DB1E5",
            "#73ACE1","#7AA8DD","#80A3D9","#869FD4","#8D9BD0",
            "#9396CC","#9992C7","#A08DC3","#A689BF","#AC84BB",
            "#B380B6","#B97BB2","#BF77AE","#C673AA"
        };

        #endregion

        #region State

        public static string Prefix => IsCommandValid
            ? $"{PinkColor}{PrefixContent}{ResetColor}"
            : $"{RedColor}{PrefixContent}{ResetColor}";

        public static bool IsCommandValid { get; private set; } = true;

        private const int HistoryMaxCount = 10;

        public static List<char> Input { get; set; } = [];
        private static int CursorIndex { get; set; }

        private static readonly List<string> InputHistory = [];
        private static int HistoryIndex = -1;

        private static readonly object ConsoleSync = new();

        public static event Action<string>? OnConsoleExcuteCommand;

        #endregion

        #region Init & Banner

        public static void InitConsole()
        {
            if (IsRedirected()) return;

            Console.Title = "March7thHoney";
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintBanner();
        }

        public static void PrintBanner()
        {
            var markup = BuildGradientMarkup("March7thHoney");
            var panel = new Panel(new Markup(markup))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.FromHex("#60BAEE")),
                Padding = new Padding(2, 0, 2, 0)
            };

            AnsiConsole.Write(panel);
        }

        private static string BuildGradientMarkup(string text)
        {
            var parts = new List<string>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                var color = BannerPalette[i % BannerPalette.Length];
                parts.Add($"[{color}]{Markup.Escape(text[i].ToString())}[/]");
            }
            return string.Join(string.Empty, parts);
        }

        #endregion

        #region Width & Validation

        public static int GetWidth(string str)
            => str.Sum(c => c <= 0x7F ? 1 : 2);

        private static void UpdateCommandValidity(string input)
            => IsCommandValid = CheckCommandValid(input);

        private static bool CheckCommandValid(string input)
        {
            if (string.IsNullOrEmpty(input)) return true;
            var invalid = new[] { '@', '#', '$', '%', '&', '*' };
            return !invalid.Any(input.Contains);
        }

        #endregion

        #region Redraw (4.0 Core)

        public static void RedrawInput(List<char> input, bool hasPrefix = true)
            => RedrawInput(new string([.. input]), hasPrefix);

        public static void RedrawInput(string input, bool hasPrefix = true)
        {
            if (IsRedirected()) return;

            lock (ConsoleSync)
            {
                UpdateCommandValidity(input);

                var line = hasPrefix ? Prefix + input : input;
                var totalWidth = GetWidth(line);
                var cursorPos = CursorIndex + (hasPrefix ? GetWidth(PrefixContent) : 0);

                Console.Write('\r');
                Console.Write(line);
                Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - totalWidth)));
                Console.Write('\r');

                if (cursorPos < Console.BufferWidth)
                    Console.Write($"\x1b[{cursorPos}C");
            }
        }

        #endregion

        #region External Output

        public static void WriteExternalLine(Action writer, string plain)
        {
            lock (ConsoleSync)
            {
                Console.WriteLine();
                writer();
                RedrawInput(Input);
            }
        }

        #endregion

        #region Input Handling

        public static void HandleEnter()
        {
            var input = new string([.. Input]).Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            Console.WriteLine();
            Input = [];
            CursorIndex = 0;

            if (InputHistory.Count >= HistoryMaxCount)
                InputHistory.RemoveAt(0);

            InputHistory.Add(input);
            HistoryIndex = InputHistory.Count;

            if (input.StartsWith('/'))
                input = input[1..].Trim();

            OnConsoleExcuteCommand?.Invoke(input);
            IsCommandValid = true;
        }

        public static void HandleBackspace()
        {
            if (CursorIndex <= 0) return;
            CursorIndex--;
            Input.RemoveAt(CursorIndex);
            RedrawInput(Input);
        }

        public static void HandleLeftArrow()
        {
            if (CursorIndex > 0) CursorIndex--;
            RedrawInput(Input);
        }

        public static void HandleRightArrow()
        {
            if (CursorIndex < Input.Count) CursorIndex++;
            RedrawInput(Input);
        }

        public static void HandleUpArrow()
        {
            if (HistoryIndex <= 0 || InputHistory.Count == 0) return;

            HistoryIndex--;
            var history = InputHistory[HistoryIndex];
            Input = [.. history];
            CursorIndex = Input.Count;
            RedrawInput(Input);
        }

        public static void HandleDownArrow()
        {
            if (HistoryIndex >= InputHistory.Count) return;

            HistoryIndex++;
            if (HistoryIndex >= InputHistory.Count)
            {
                HistoryIndex = InputHistory.Count;
                Input = [];
                CursorIndex = 0;
                IsCommandValid = true;
            }
            else
            {
                var history = InputHistory[HistoryIndex];
                Input = [.. history];
                CursorIndex = Input.Count;
            }

            RedrawInput(Input);
        }

        public static void HandleInput(ConsoleKeyInfo key)
        {
            if (char.IsControl(key.KeyChar)) return;
            if (char.IsSurrogate(key.KeyChar)) return;

            var newWidth = GetWidth(new string([.. Input]))
                         + GetWidth(key.KeyChar.ToString());

            if (newWidth >= Console.BufferWidth - GetWidth(PrefixContent))
                return;

            HandleInput(key.KeyChar);
        }

        public static void HandleInput(char ch)
        {
            Input.Insert(CursorIndex, ch);
            CursorIndex++;
            RedrawInput(Input);
        }

        #endregion

        #region Main Loop

        public static void ListenConsole()
        {
            if (IsRedirected()) return;

            while (true)
            {
                ConsoleKeyInfo key;
                try { key = Console.ReadKey(true); }
                catch (InvalidOperationException) { continue; }

                lock (ConsoleSync)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.Enter: HandleEnter(); break;
                        case ConsoleKey.Backspace: HandleBackspace(); break;
                        case ConsoleKey.LeftArrow: HandleLeftArrow(); break;
                        case ConsoleKey.RightArrow: HandleRightArrow(); break;
                        case ConsoleKey.UpArrow: HandleUpArrow(); break;
                        case ConsoleKey.DownArrow: HandleDownArrow(); break;
                        default: HandleInput(key); break;
                    }
                }
            }
        }

        #endregion

        #region Helpers

        private static bool IsRedirected()
            => Console.IsInputRedirected || Console.IsOutputRedirected;

        #endregion
    }
}

