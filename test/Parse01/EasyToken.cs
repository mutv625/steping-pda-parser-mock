using System;
using System.Collections.Generic;

namespace Parse01
{
    class Entry
    {
        public static void Point(string[] args)
        {
        }
    }

    // 式全体を表すノード
    public class ExpressionNode : INode
    {
        public INode? ParentNode { get; private set; }
        public bool IsClosed { get; private set; } = false;

        // パース結果を格納するプロパティ群
        public List<int> IntValues { get; } = new();

        public void InitializeNode(INode parentNode)
        {
            ParentNode = parentNode;
        }

        public void Close() => IsClosed = true;

        public string Serialize() => $"ExpressionNode:[{string.Join(", ", IntValues)}]";
    }

    // 内部で使う値保持用の一時的なノード
    public class IntHolderNode : IValueNode<int>
    {
        public INode? ParentNode { get; private set; }
        public bool IsClosed => false;
        public int Value { get; set; }

        public void InitializeNode(INode parentNode) => ParentNode = parentNode;
        public string Serialize() => Value.ToString();
    }


    // 1. エントリーポイントとなるタスク
    public class ExpressionParseTask : UserTask
    {
        private readonly Parser _parser;
        private readonly ExpressionNode _exprNode;

        public ExpressionParseTask(Parser parser, ExpressionNode node) : base(parser, node)
        {
            _parser = parser;
            _exprNode = node;
        }

        public override List<IParseTask> UserProcess()
        {
            // 先読み(Lookahead)用のMatchDictを構築
            // パターンA: int が 2つ続く場合
            var patternA = new PatternList()
                .AddPattern<Token<int>>()
                .AddPattern<Token<int>>()
                .FollowedBy(new List<IParseTask> { new ExpressionMainTask(_parser, _exprNode, hasOptionalBlock: true) });

            // パターンB: int が 1つだけ（フォールバック）
            var patternB = new PatternList()
                .AddPattern<Token<int>>()
                .FollowedBy(new List<IParseTask> { new ExpressionMainTask(_parser, _exprNode, hasOptionalBlock: false) });

            var matchDict = new MatchDict(new List<LockedPatternList> { patternA, patternB });

            // 最大2トークン先読みすれば判定できるので、Length = 2 で MatchTask をスタックに積む
            return new List<IParseTask> { new MatchTask(length: 2, matchDict, _parser) };
        }
    }

    // 2. 分岐した後の実際のパース（値の回収）を行うタスク
    public class ExpressionMainTask : IParseTask
    {
        private readonly Parser _parser;
        private readonly ExpressionNode _node;
        private readonly bool _hasOptionalBlock;

        public ExpressionMainTask(Parser parser, ExpressionNode node, bool hasOptionalBlock)
        {
            _parser = parser;
            _node = node;
            _hasOptionalBlock = hasOptionalBlock;
        }

        public List<IParseTask> Process()
        {
            var tasks = new List<IParseTask>();

            if (_hasOptionalBlock)
            {
                // (<int> <int>) のルート
                // 3つの int を順番に回収するタスクを組み立てる
                var holder1 = new IntHolderNode();
                var holder2 = new IntHolderNode();
                var holder3 = new IntHolderNode();

                // 注意: スタック（SinkRange）に積まれるので、実行させたい順に List に格納する
                // （SinkRange 内部で Reverse されるため、これで上から順番に実行されます）
                tasks.Add(new GotoTask(holder1, _parser));
                tasks.Add(new SetValueTask<int>(_parser));
                tasks.Add(new ActionTask(() => _node.IntValues.Add(holder1.Value))); // 値を本体ノードに回収

                tasks.Add(new GotoTask(holder2, _parser));
                tasks.Add(new SetValueTask<int>(_parser));
                tasks.Add(new ActionTask(() => _node.IntValues.Add(holder2.Value)));

                tasks.Add(new GotoTask(holder3, _parser));
                tasks.Add(new SetValueTask<int>(_parser));
                tasks.Add(new ActionTask(() => _node.IntValues.Add(holder3.Value)));
            }
            else
            {
                // <int> だけのルート
                var holder1 = new IntHolderNode();

                tasks.Add(new GotoTask(holder1, _parser));
                tasks.Add(new SetValueTask<int>(_parser));
                tasks.Add(new ActionTask(() => _node.IntValues.Add(holder1.Value)));
            }

            // 最後にこのノードを閉じて親に戻るタスクを添える
            tasks.Add(new ActionTask(() => _node.Close()));
            tasks.Add(new ReturnTask(_parser));

            return tasks;
        }
    }

    // 汎用ユーティリティ：ラムダ式でちょっとした処理を挟むための軽量タスク
    public class ActionTask : IParseTask
    {
        private readonly Action _action;
        public ActionTask(Action action) => _action = action;
        public List<IParseTask> Process()
        {
            _action();
            return new List<IParseTask>();
        }
    }
}