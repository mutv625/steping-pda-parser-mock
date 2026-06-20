using System;
using System.Collections.Generic;
using System.Linq;

namespace Parse01
{
    public class Entry
    {
        public static void Exec()
        {
            Console.WriteLine("=====================================");
            Console.WriteLine("  PDA パーサー 動作検証テスト");
            Console.WriteLine("=====================================");

            // -------------------------------------------------------------
            // 1. テスト用トークン列の準備: "myFunc(10, 20)"
            // -------------------------------------------------------------
            var tokens = new List<IToken>
            {
                new ValueToken<string>("myFunc"), // <str>
                new OpenParenToken(),             // '('
                new ValueToken<int>(10),          // <int>
                new CommaToken(),                 // ','
                new ValueToken<int>(20),          // <int>
                new CommaToken(),                 // ','
                new ValueToken<int>(0),          // <int>
                new CloseParenToken()             // ')'
            };

            Console.WriteLine("入力トークン: myFunc(10, 20, 0)");

            // -------------------------------------------------------------
            // 2. 構文ノードの初期化
            // -------------------------------------------------------------
            // TODO RootNode の扱いと初期化における強制
            var rootNode = new MethodCallNode(parentNode: null);

            // -------------------------------------------------------------
            // 3. パーサーのインスタンス化
            // -------------------------------------------------------------
            Parser parser = new Parser();

            // パーサーのインスタンスが確定したので、本物の初期タスクを安全に生成できます
            var initTask = new MethodCallParseTask(parser, rootNode);

            // パーサーのInitialize()であとから初期化
            parser.Initialize(tokens, initTask, rootNode);

            // -------------------------------------------------------------
            // 4. パースループの実行 (プッシュダウンオートマトン駆動)
            // -------------------------------------------------------------
            Console.WriteLine("\nパース処理を開始します...");
            
            try
            {
                int step = 1;
                bool isFinalStep = false;
                // スタックが空になるまで、1ステップずつ Process() を回す
                while (!isFinalStep)
                {
                    
                        
                    // デバッグ用：現在のポインタ位置とスタック数を可視化
                    Console.WriteLine($"[Step {step:D2}] PointingNode: {parser.PointingNode.GetType().Name,-18}");
                    Console.WriteLine($"          | Next Token ({parser.PtrIndex}): {(parser.PtrIndex < parser.Tokens.Count ? parser.Tokens[parser.PtrIndex].GetType().GetPrettyName() : "EOF"),-18}");
                    Console.WriteLine($"          | Call Stack ({parser.ParseTasksStack.Count}) [[ {string.Join(" <- ", parser.ParseTasksStack.Reverse().Select(t => t.GetType().GetPrettyName()))}");

                    Console.WriteLine($"          | JSON: {rootNode.Serialize()}");

                    if (parser.IsParseTasksEmpty)
                    {
                        isFinalStep = true;
                        break;
                    }
                    
                    parser.Process();
                    step++;
                }

                Console.WriteLine("\n🎉 パースが正常に完了しました！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ パース中にエラーが発生しました: {ex.Message}");
                Console.WriteLine( rootNode.Serialize());
                return;
            }

            // -------------------------------------------------------------
            // 5. 評価結果のシリアライズ出力
            // -------------------------------------------------------------
            Console.WriteLine("\n=====================================");
            Console.WriteLine("  パース結果 (AST JSON)");
            Console.WriteLine("=====================================");
            
            string jsonOutput = rootNode.Serialize();
            Console.WriteLine(jsonOutput);
        }
    }

    public static class TypeExtensions
    {
        public static string GetPrettyName(this Type type)
        {
            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments().Select(t => t.GetPrettyName());
                var name = type.GetGenericTypeDefinition().Name;
                // ` の前の部分を取得
                name = name.Substring(0, name.IndexOf('`'));
                return $"{name}<{string.Join(", ", genericArgs)}>";
            }
            return type.Name;
        }
    }


    // == A. トークンの定義

    #region Additional Tokens
    public class OpenParenToken  : IToken { public object? Value => "("; }
    public class CloseParenToken : IToken { public object? Value => ")"; }
    public class CommaToken      : IToken { public object? Value => ","; }
    #endregion


    // == B. 構文ノードの定義

    #region Custom Node
    public class MethodCallNode : BaseNode
    {
        // 基盤側の ValueSettableNode<T> を内包する
        public ValueNode<string>? MethodName { get; private set; }
        public ValueListNode<int>? Arguments { get; private set; }

        public MethodCallNode(BaseNode? parentNode) : base(parentNode) { }

        protected override void OnFirstVisit()
        {
            // 訪れた瞬間に直属の子ノードのみを「あらしめる」
            MethodName = new ValueNode<string>(this);
            Arguments = new ValueListNode<int>(this);
        }

        public override string Serialize()
        {
            var nameJson = MethodName != null ? MethodName.Serialize() : "\"uninitialized\"";
            var argsJson = Arguments != null ? Arguments.Serialize() : "\"uninitialized\"";
            return $"{{" +
                $"\"MethodName\": {nameJson}, " +
                $"\"Arguments\": {argsJson}" +
                $"}}";
        }
    }
    #endregion


    // == C. パースタスクの定義

    #region Custom Tasks
    public class MethodCallParseTask : UserTask
    {
        private readonly MethodCallNode _workingMethodNode;

        public MethodCallParseTask(Parser p, MethodCallNode nodeToVisit) 
            : base(p, nodeToVisit)
        {
            _workingMethodNode = nodeToVisit;
        }

        public override List<IParseTask> UserProcess()
        {
            return new List<IParseTask>
            {
                // 1. <str> (メソッド名) のパース
                new ParseValueTask<ValueToken<string>, string>(_parser, _workingMethodNode.MethodName!),
                
                // 2. '(' を消費
                new ExpectTokenTask<OpenParenToken>(_parser),
                
                // 3. 最初の <int> (最低1つの引数) をパース
                // ★基盤の共通化のおかげで、ValueListNode をそのまま直接渡せる！
                new ParseValueTask<ValueToken<int>, int>(_parser, _workingMethodNode.Arguments!),
                
                // 4. ( ',' <int> )* ')' のループ + 閉じ括弧判定・制御タスクへ移行
                // ★ 第2引数に _workingMethodNode を指定してタスクを作成
                new ArgLoopDecisionTask(_parser, _workingMethodNode, _workingMethodNode.Arguments!),

                // 5. 全部終わったら本来return（今回はrootの設定がガバなので飛ばす）
                // new ReturnTask(_parser)
            };
        }
    }

    public class ArgLoopDecisionTask : UserTask // ★ IParseTask から UserTask 継承へ変更
    {
        private readonly ValueListNode<int> _argumentsNode;

        // コンストラクタで、このループが所属する中央司令塔（MethodCallNode）を nodeToVisit として受け取る
        // ! 自分自身にとどまるという表現の副作用として、このループが変なところに飛んでいくリスクが減る
        public ArgLoopDecisionTask(Parser p, MethodCallNode methodNodeToVisit, ValueListNode<int> argumentsNode) 
            : base(p, methodNodeToVisit) // 親の UserTask に MethodCallNode を渡す
        {
            _argumentsNode = argumentsNode;
        }

        // Process() は UserTask が肩代わりするため、UserProcess() をオーバーライド
        public override List<IParseTask> UserProcess()
        {
            var matchDict = new MatchDict(new List<LockedPatternList>
            {
                // パターンA: カンマ検知でループ継続
                new PatternList()
                    .AddPattern<CommaToken>()
                    .FollowedBy(new List<IParseTask>
                    {
                        new ExpectTokenTask<CommaToken>(_parser),
                        new ParseValueTask<ValueToken<int>, int>(_parser, _argumentsNode),
                        this // 自分自身を再装填。UserTaskなので、次回起動時も確実に MethodCallNode に移動する
                    }),

                // パターンB: 閉じ括弧検知でループ終了
                new PatternList()
                    .AddPattern<CloseParenToken>()
                    .FollowedBy(new List<IParseTask>
                    {
                        new ExpectTokenTask<CloseParenToken>(_parser),
                    })
            });

            return new List<IParseTask> { new MatchTask(_parser, 1, matchDict) };
        }
    }
    #endregion
}