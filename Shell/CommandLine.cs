using System;
using System.Collections.Generic;
using System.Text;

namespace StyleOS
{
    public enum ChainOp { None, And, Or, Semicolon }

    /// <summary>One stage of a pipeline: a command plus its arguments and redirections.</summary>
    public class CommandStage
    {
        public List<string> Tokens = new List<string>();
        public string RedirectOut;      // > file
        public string RedirectAppend;   // >> file
        public string RedirectIn;       // < file
        public string Name => Tokens.Count > 0 ? Tokens[0] : "";
        public List<string> Args => Tokens.Count > 1 ? Tokens.GetRange(1, Tokens.Count - 1) : new List<string>();
    }

    /// <summary>A pipeline (a | b | c) plus the operator that joins it to the next one.</summary>
    public class Pipeline
    {
        public List<CommandStage> Stages = new List<CommandStage>();
        public ChainOp NextOp = ChainOp.None;
    }

    public static class CommandLine
    {
        /// <summary>Splits raw input into pipelines chained by ;, &amp;&amp; and ||.</summary>
        public static List<Pipeline> Parse(string input)
        {
            var pipelines = new List<Pipeline>();
            if (string.IsNullOrWhiteSpace(input)) return pipelines;

            foreach (var chunk in SplitChain(input))
            {
                var pipeline = new Pipeline { NextOp = chunk.Op };
                foreach (string stageText in SplitTop(chunk.Text, '|'))
                {
                    var stage = BuildStage(stageText);
                    if (stage.Tokens.Count > 0) pipeline.Stages.Add(stage);
                }
                if (pipeline.Stages.Count > 0) pipelines.Add(pipeline);
            }
            return pipelines;
        }

        private struct Chunk
        {
            public string Text;
            public ChainOp Op;
        }

        private static List<Chunk> SplitChain(string input)
        {
            var result = new List<Chunk>();
            var current = new StringBuilder();
            char quote = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '\\' && i + 1 < input.Length && quote != '\'')
                {
                    current.Append(c).Append(input[i + 1]);
                    i++;
                    continue;
                }

                if (quote == '\0' && (c == '"' || c == '\'')) { quote = c; current.Append(c); continue; }
                if (quote != '\0' && c == quote) { quote = '\0'; current.Append(c); continue; }

                if (quote == '\0')
                {
                    if (c == '&' && i + 1 < input.Length && input[i + 1] == '&')
                    {
                        result.Add(new Chunk { Text = current.ToString(), Op = ChainOp.And });
                        current.Clear(); i++; continue;
                    }
                    if (c == '|' && i + 1 < input.Length && input[i + 1] == '|')
                    {
                        result.Add(new Chunk { Text = current.ToString(), Op = ChainOp.Or });
                        current.Clear(); i++; continue;
                    }
                    if (c == ';')
                    {
                        result.Add(new Chunk { Text = current.ToString(), Op = ChainOp.Semicolon });
                        current.Clear(); continue;
                    }
                }

                current.Append(c);
            }

            if (current.Length > 0) result.Add(new Chunk { Text = current.ToString(), Op = ChainOp.None });

            // The operator belongs to the chunk *before* it, shift them one to the left.
            var shifted = new List<Chunk>();
            for (int i = 0; i < result.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(result[i].Text)) continue;
                shifted.Add(new Chunk { Text = result[i].Text, Op = result[i].Op });
            }
            return shifted;
        }

        /// <summary>Splits on a separator that is not inside quotes and not doubled (|| is not a pipe).</summary>
        private static List<string> SplitTop(string input, char separator)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '\\' && i + 1 < input.Length && quote != '\'')
                {
                    current.Append(c).Append(input[i + 1]);
                    i++;
                    continue;
                }

                if (quote == '\0' && (c == '"' || c == '\'')) { quote = c; current.Append(c); continue; }
                if (quote != '\0' && c == quote) { quote = '\0'; current.Append(c); continue; }

                if (quote == '\0' && c == separator)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private static CommandStage BuildStage(string text)
        {
            var stage = new CommandStage();
            var raw = Tokenize(text);

            for (int i = 0; i < raw.Count; i++)
            {
                string t = raw[i];

                if (t == ">" || t == ">>" || t == "<")
                {
                    if (i + 1 >= raw.Count) break;
                    string target = ShellEnv.Expand(raw[i + 1]);
                    if (t == ">") stage.RedirectOut = target;
                    else if (t == ">>") stage.RedirectAppend = target;
                    else stage.RedirectIn = target;
                    i++;
                    continue;
                }

                if (t.Length > 1 && t[0] == '>' && t != ">>")
                {
                    stage.RedirectOut = ShellEnv.Expand(t.Substring(1));
                    continue;
                }
                if (t.Length > 2 && t.StartsWith(">>"))
                {
                    stage.RedirectAppend = ShellEnv.Expand(t.Substring(2));
                    continue;
                }
                if (t.Length > 1 && t[0] == '<')
                {
                    stage.RedirectIn = ShellEnv.Expand(t.Substring(1));
                    continue;
                }

                stage.Tokens.Add(t);
            }
            return stage;
        }

        /// <summary>
        /// Word splitting with '...' (literal), "..." (expanded) and backslash escapes.
        /// The old parser toggled on every quote character and could not escape anything.
        /// </summary>
        public static List<string> Tokenize(string input)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool has = false;
            char quote = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '\\' && i + 1 < input.Length && quote != '\'')
                {
                    char next = input[i + 1];

                    // StyleOS runs on Windows, where '\' is the path separator, so an
                    // unquoted backslash must stay literal ("cd C:\Users\tim" would
                    // otherwise get eaten character by character). Only inside double
                    // quotes do the classic \" \\ \$ \` escapes still apply; anything
                    // else (paths, or \n \t meant for printf/echo -e) keeps its backslash.
                    bool isRealEscape = quote == '"' &&
                        (next == '"' || next == '\\' || next == '$' || next == '`');

                    if (isRealEscape)
                    {
                        current.Append(next);
                        has = true;
                        i++;
                        continue;
                    }

                    // Not a recognized escape here: keep the backslash itself and let the
                    // following character go through the normal path on the next iteration.
                    current.Append(c);
                    has = true;
                    continue;
                }

                if (quote == '\0' && (c == '"' || c == '\''))
                {
                    quote = c;
                    has = true;
                    continue;
                }

                if (quote != '\0' && c == quote)
                {
                    quote = '\0';
                    continue;
                }

                // Inside single quotes nothing expands, so hide the $ from the expander
                // and put it back once expansion is done.
                if (quote == '\'' && c == '$')
                {
                    current.Append('\x01');
                    has = true;
                    continue;
                }

                if (quote == '\0' && char.IsWhiteSpace(c))
                {
                    if (has) { result.Add(current.ToString()); current.Clear(); has = false; }
                    continue;
                }

                // Keep redirections as separate tokens even when glued to a word.
                if (quote == '\0' && (c == '>' || c == '<'))
                {
                    if (has) { result.Add(current.ToString()); current.Clear(); has = false; }
                    if (c == '>' && i + 1 < input.Length && input[i + 1] == '>') { result.Add(">>"); i++; }
                    else result.Add(c.ToString());
                    continue;
                }

                current.Append(c);
                has = true;
            }

            if (has) result.Add(current.ToString());

            for (int i = 0; i < result.Count; i++)
            {
                if (result[i] == ">" || result[i] == ">>" || result[i] == "<") continue;
                result[i] = ShellEnv.Expand(result[i]).Replace('\x01', '$');
            }

            return result;
        }
    }
}
