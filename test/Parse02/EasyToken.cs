using System;
using System.Collections.Generic;

namespace Parse02
{
    class Entry
    {
        // == トークンの定義（コンパイル通す用）
        public class OpenParenToken : IToken { public object? Value => null; }
        public class CloseParenToken : IToken { public object? Value => null; }
        public class CommaToken : IToken { public object? Value => null; }

        public static void Exec()
        {
            // 1. テスト用トークン列: "myFunc(10, 20)"
            var tokens = new List<IToken>
            {
                new ValueToken<string>("myFunc"),
                new OpenParenToken(),
                new ValueToken<int>(10),
                new CommaToken(),
                new ValueToken<int>(20),
                new CloseParenToken()
            };

            var parser = new Parser();
            var rootNode = new MethodCallNode(parentNode: null);

            // -------------------------------------------------------------
            // 2. 💡 NodeTask を使った「データ駆動」による文法定義
            // -------------------------------------------------------------
            
            // 再帰（ループ）のための変数宣言
            NodeTask argLoopTask = null!;

            // ループタスクの定義（ラムダ式にすることで、内部での argLoopTask の参照を遅延評価する）
            argLoopTask = new NodeTask(parser, rootNode, () => new List<IParseTask>
            {
                new MatchTask(parser, 1, new MatchDict(new List<LockedPatternList>
                {
                    // パターンA: カンマが来たら、カンマを消費して次の引数をパースし、再度ループ
                    new PatternList().AddPattern<CommaToken>()
                    .FollowedBy(new List<IParseTask>
                    {
                        new ExpectTokenTask<CommaToken>(parser),
                        new ParseValueTask<ValueToken<int>, int>(parser, rootNode.Arguments!),
                        argLoopTask // ★ 自分自身を再装填（遅延評価なので安全！）
                    }),

                    // パターンB: 閉じ括弧が来たら、閉じ括弧を消費して呼び出し元に戻る
                    new PatternList().AddPattern<CloseParenToken>()
                    .FollowedBy(new List<IParseTask>
                    {
                        new ExpectTokenTask<CloseParenToken>(parser),
                        new ReturnTask(parser)
                    })
                }))
            });

            // 最上位の構文ルール（メソッド呼び出し全体）の定義
            var initTask = new NodeTask(parser, rootNode, () => new List<IParseTask>
            {
                new ParseValueTask<ValueToken<string>, string>(parser, rootNode.MethodName!), // 1. メソッド名パース
                new ExpectTokenTask<OpenParenToken>(parser),                                  // 2. '(' を期待
                new ParseValueTask<ValueToken<int>, int>(parser, rootNode.Arguments!),        // 3. 第1引数パース
                argLoopTask                                                                   // 4. 引数ループへ突入
            });

            // -------------------------------------------------------------
            // 3. 実行
            // -------------------------------------------------------------
            var tester = new ParserTester(parser, tokens, initTask, rootNode);
            tester.Run();
        }
    }

    // ※ テスト実行のために前回の MethodCallNode を模した定義
    public class MethodCallNode : BNode
    {
        public ValueNode<string>? MethodName { get; private set; }
        public ValueListNode<int>? Arguments { get; private set; }
        public MethodCallNode(BNode? parentNode) : base(parentNode) { }
        protected override void OnFirstVisit()
        {
            MethodName = new ValueNode<string>(this);
            Arguments = new ValueListNode<int>(this);
        }
        public override string Serialize() => $"{{\"MethodName\": {MethodName?.Serialize()}, \"Arguments\": {Arguments?.Serialize()}}}";
    }
}