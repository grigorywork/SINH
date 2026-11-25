namespace Sinh.Core;

internal static class TreePolicy
{
    private static bool Within(string root,string path)=>string.Equals(root,path,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static (TreeSnapshot A,TreeSnapshot B) ProtectLinks(TreeSnapshot a,TreeSnapshot b)
    {
        var links=(a.LinkPaths??[]).Concat(b.LinkPaths??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(links.Length==0)return(a,b);
        TreeSnapshot Filter(TreeSnapshot tree)=>TreeSnapshot.Create(tree.Root,tree.Entries.Values.Where(e=>!links.Any(link=>Within(link,e.RelativePath))),(tree.SkippedPaths??[]).Concat(links),links);
        return(Filter(a),Filter(b));
    }
    internal static HashSet<string> ProtectedParents(params TreeSnapshot[] trees)
    {
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in trees.SelectMany(t=>t.SkippedPaths??[]))
        {
            var parent=Path.GetDirectoryName(path);
            while(!string.IsNullOrEmpty(parent)){result.Add(parent);parent=Path.GetDirectoryName(parent);}
        }
        return result;
    }
    internal static void PreserveContainers(Dictionary<string,DesiredVersion> desired,params TreeSnapshot[] trees)
    {
        var protectedParents=ProtectedParents(trees);
        if(protectedParents.Count==0)return;
        var existing=desired.ToDictionary(k=>k.Key,k=>k.Value,StringComparer.OrdinalIgnoreCase);
        foreach(var tree in trees)
        foreach(var entry in tree.Entries.Values.Where(e=>e.Directory&&protectedParents.Contains(e.RelativePath)))
        {
            if(existing.TryGetValue(entry.RelativePath,out var current))
            {
                if(!current.Entry.Directory||current.Entry.RelativePath!=entry.RelativePath)throw new IOException("Изменение затрагивает каталог с исключёнными данными или ссылками: "+entry.RelativePath);
                continue;
            }
            var value=new DesiredVersion(entry,tree.Root,entry.RelativePath);desired[entry.RelativePath]=value;existing[entry.RelativePath]=value;
        }
    }
}
