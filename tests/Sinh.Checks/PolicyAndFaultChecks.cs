using Sinh.Core;

internal static class PolicyAndFaultChecks
{
    internal static async Task Run(Func<string,Func<Task>,Task> check,Action<bool,string> require,string temporary)
    {
        (string A,string B,string Data) Setup()
        {
            var root=Path.Combine(temporary,Guid.NewGuid().ToString("N"));var a=Path.Combine(root,"A");var b=Path.Combine(root,"B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);return(a,b,Path.Combine(root,"data"));
        }
        await check("review policy distinguishes ordinary runs, USB direction and two-way conflicts",async()=>
        {
            var(a,b,_)=Setup();await File.WriteAllTextAsync(Path.Combine(a,"file"),"data");var request=new SyncRequest(Guid.NewGuid(),a,b);var plan=await new ComparisonService().CompareAsync(request);
            require(PlanReviewPolicy.RequiresReview(plan,false,plan.Fingerprint),"Manual preview bypassed");
            require(PlanReviewPolicy.RequiresReview(plan,true,null),"New profile auto-applied");
            require(!PlanReviewPolicy.RequiresReview(plan,true,plan.Fingerprint),"Ordinary configured run asks again");
            require(PlanReviewPolicy.RequiresReview(plan with {DestinationChanged=true},true,plan.Fingerprint),"USB changes auto-overwritten");
            var two=request with {Mode=SyncMode.TwoWay};var twoPlan=plan with {Request=two,Fingerprint=SyncPlanner.Fingerprint(two),DestinationChanged=true};
            require(!PlanReviewPolicy.RequiresReview(twoPlan,true,twoPlan.Fingerprint),"Unambiguous two-way update unnecessarily blocked");
            require(PlanReviewPolicy.RequiresReview(twoPlan with {Conflicts=[new("file","both")]},true,twoPlan.Fingerprint),"Ambiguous conflict not reviewed");
            var reverse=request with {Source=b,Destination=a};
            require(PlanReviewPolicy.RequiresReview(plan with {Request=reverse,Fingerprint=SyncPlanner.Fingerprint(reverse)},true,plan.Fingerprint),"Direction change bypassed concrete preview");
        });
        await check("mass deletion requires review after configured mirror",async()=>
        {
            var(a,b,_)=Setup();for(var i=0;i<12;i++)await File.WriteAllTextAsync(Path.Combine(b,"old-"+i),"x");var request=new SyncRequest(Guid.NewGuid(),a,b,SyncMode.Mirror,true);var plan=await new ComparisonService().CompareAsync(request);
            require(PlanReviewPolicy.IsMassDeletion(plan)&&PlanReviewPolicy.RequiresReview(plan,true,plan.Fingerprint),"Mass deletion auto-applied");
        });
        await check("forged copy cannot read outside configured snapshots",async()=>
        {
            var(a,b,data)=Setup();var outside=Path.Combine(Path.GetDirectoryName(a)!,"outside.txt");await File.WriteAllTextAsync(outside,"outside configured roots");
            var request=new SyncRequest(Guid.NewGuid(),a,b);var plan=await new ComparisonService().CompareAsync(request);var entry=await TreeScanner.ReadFileAsync(outside,"copied.txt");
            var forged=plan with {Operations=[new(OperationKind.CopyFile,plan.Destination.Root,"copied.txt",null,entry,Path.GetDirectoryName(outside),Path.GetFileName(outside))]};var failed=false;
            try{await new SyncExecutor(new(data)).ApplyAsync(forged);}catch(IOException){failed=true;}
            require(failed&&!File.Exists(Path.Combine(b,"copied.txt")),"Unconfigured source was copied");
        });
        await check("drive identity change before commit preserves destination",async()=>
        {
            var(a,b,data)=Setup();await File.WriteAllTextAsync(Path.Combine(a,"file"),"new");await File.WriteAllTextAsync(Path.Combine(b,"file"),"old");var plan=await new ComparisonService().CompareAsync(new(Guid.NewGuid(),a,b));var calls=0;var failed=false;
            try{await new SyncExecutor(new(data)).ApplyAsync(plan,new(ContextAvailable:()=>Interlocked.Increment(ref calls)<3));}catch(IOException){failed=true;}
            require(failed&&await File.ReadAllTextAsync(Path.Combine(b,"file"))=="old","Wrong/disconnected drive was modified");
        });
    }
}
