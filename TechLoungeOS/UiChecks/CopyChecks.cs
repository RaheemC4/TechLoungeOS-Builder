using System.Windows;
using System.Windows.Controls;

internal static class CopyChecks
{
    internal static void Run(DependencyObject root)
    {
        var labels = new List<string>();
        void Visit(DependencyObject node)
        {
            if (node is TextBlock text) labels.Add(text.Text);
            if (node is ContentControl { Content: string content }) labels.Add(content);
            if (node is HeaderedContentControl { Header: string header }) labels.Add(header);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Visit(child);
        }
        Visit(root);
        var broken = labels.FirstOrDefault(s => s.Contains("â€", StringComparison.Ordinal) ||
            s.Contains("â†", StringComparison.Ordinal) || s.Contains("Â·", StringComparison.Ordinal) || s.Contains('\uFFFD'));
        if (broken != null) throw new IOException("Visible interface text has broken Unicode: " + broken);
        if (!labels.Contains("Browse…") || !labels.Contains("Choose location…"))
            throw new IOException("Native browse/save actions lost their expected ellipsis text.");
        Console.WriteLine("Actual compiled WPF action labels and static captions preserve Unicode.");
    }
}
