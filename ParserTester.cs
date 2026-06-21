using System;
using System.Collections.Generic;
using System.Linq;

public class ParserTester
{
    Parser parser;

    public ParserTester(Parser parser, List<IToken> tokens, IParseTask initialTask, BNode rootNode)
    {
        this.parser = parser;
        this.parser.Initialize(tokens, initialTask, rootNode);
    }


    public int Run()
    {
        Console.WriteLine("====================================");
        Console.WriteLine("  ParserTester [ Activated ]");
        Console.WriteLine("====================================");
        Console.WriteLine("\nパース処理を開始します...");
            
        try
        {
            int step = 1;
            // スタックが空になるまで、1ステップずつ Process() を回す
            while (true)
            {
                // デバッグ用：現在のポインタ位置とスタック数を可視化
                Console.WriteLine($"[Step {step:D2}] PointingNode: {parser.PointingNode.GetType().Name,-18}");
                Console.WriteLine($"          | Next Token ({parser.PtrIndex}): {(parser.PtrIndex < parser.Tokens.Count ? parser.Tokens[parser.PtrIndex].GetType().GetPrettyName() : "EOF"),-18}");
                Console.WriteLine($"          | Call Stack ({parser.ParseTasksStack.Count}) [[ {string.Join(" <- ", parser.ParseTasksStack.Reverse().Select(t => t.GetType().GetPrettyName()))}");

                Console.WriteLine($"          | JSON: {parser.RootNode.Serialize()}");

                if (parser.IsParseTasksEmpty) break;

                parser.Process();
                step++;
            }

            Console.WriteLine("\n🎉 パースが正常に完了しました！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ パース中にエラーが発生しました: {ex.Message}");
            Console.WriteLine(parser.RootNode.Serialize());
            return -1;
        }

        // -------------------------------------------------------------
        // 5. 評価結果のシリアライズ出力
        // -------------------------------------------------------------
        Console.WriteLine($"\n✅ パースが成功しました！");
        Console.WriteLine("\n=====================================");
        Console.WriteLine("  パース結果 (AST JSON)");
        Console.WriteLine("=====================================");
        
        string jsonOutput = parser.RootNode.Serialize();
        Console.WriteLine(jsonOutput);
        return 0;
    }
}