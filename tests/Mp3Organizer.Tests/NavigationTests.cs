using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    sealed class NavigationConsole(params string[] answers):IResolutionConsole
    {
        readonly Queue<string> inputs=new(answers);
        public List<(string Prompt,string Default)> Reads=new();
        public List<string> Events=new();
        public void Write(string text)=>Events.Add(text);
        public ResolutionInput Read(string prompt,string suggestion)
        {
            Reads.Add((prompt,suggestion));Events.Add("PROMPT:"+prompt);
            if(inputs.Count==0)throw new Exception("Unexpected navigation prompt: "+prompt);
            var text=inputs.Dequeue();return text=="BACK"?new(false,"",true):text=="ESC"?new(true,""):new(false,text);
        }
    }
    static void NavigationBackAndEdit()
    {
        var values=new[]{"Original Artist","Original Album","2000"};
        var input=new NavigationConsole("","Chosen Artist","Chosen Album","BACK","BACK","Edited Artist","","BACK","BACK","","","","ESC");
        var steps=new List<ResolutionStep>();
        for(var n=0;n<3;n++){var field=n;steps.Add(new(new[]{"Artist","Album","Year"}[n],()=>values[field],value=>values[field]=value));}
        steps.Add(new("Track",()=>"",_=>{}));
        new ResolutionNavigator(input).Run(steps,()=>{});
        Equal("Edited Artist",values[0]);Equal("Chosen Album",values[1]);Equal("2000",values[2]);
        True(input.Reads.Any(x=>x.Prompt=="Artist"&&x.Default=="Chosen Artist"));
        True(input.Reads.Any(x=>x.Prompt=="Artist"&&x.Default=="Edited Artist"));
        True(input.Reads.Where(x=>x.Prompt=="Album").Skip(1).All(x=>x.Default=="Chosen Album"));
    }
    static void NavigationFirstFieldReturnsToSelection()
    {
        var selections=0;var value="initial";
        var input=new NavigationConsole("","BACK","BACK","","corrected");
        new ResolutionNavigator(input).Run([new("Artist",()=>value,text=>value=text)],()=>selections++);
        Equal(5,selections);Equal("corrected",value);
    }
    static void NavigationFileContextAndPersistence()
    {
        var f=Fixture();var row=Unresolved(f);var store=ManualStore(f);
        var input=new NavigationConsole("","","","","Wrong title","BACK","Correct title","oops","1");
        new InteractiveResolution(store,input).Run([row],true,false);
        var saved=store.Load().Files.Values.Single();Equal("Correct title",saved.Title);Equal((uint?)1,saved.TrackNumber);
        for(var i=0;i<input.Events.Count;i++)
            if(input.Events[i].StartsWith("PROMPT:Title")||input.Events[i].StartsWith("PROMPT:Track number"))
            {
                True(i>0);var context=input.Events[i-1];True(context.Contains("File: "+row.FileName));True(context.Contains("Path: "+row.FullPath));True(context.Contains("Current:"));True(context.Contains("Artist:"));True(context.Contains("Track number:"));True(context.EndsWith("\n\n\nPath: "+row.FullPath));
            }
        True(input.Events.Any(x=>x.Contains("Title: Correct title")));
        True(input.Events.Contains("Enter = accept | Backspace = delete text | Alt+Left = previous | Esc = skip/abort folder"));
    }
    static void BackspaceEditsInsteadOfNavigating()
    {
        var keys=new Queue<ConsoleKeyInfo>(new[]{new ConsoleKeyInfo('A',ConsoleKey.A,false,false,false),new ConsoleKeyInfo('i',ConsoleKey.I,false,false,false),new ConsoleKeyInfo('\b',ConsoleKey.Backspace,false,false,false),new ConsoleKeyInfo('\b',ConsoleKey.Backspace,false,false,false),new ConsoleKeyInfo('\b',ConsoleKey.Backspace,false,false,false)});
        foreach(var c in "The Airship")keys.Enqueue(new(c,ConsoleKey.A,false,false,false));keys.Enqueue(new('\r',ConsoleKey.Enter,false,false,false));
        var previous=Console.Out;using var output=new StringWriter();Console.SetOut(output);
        try
        {
            var result=new ResolutionConsole(()=>keys.Dequeue()).Read("Title","45");Equal("The Airship",result.Text);True(!result.Back);
            result=new ResolutionConsole(()=>new('\0',ConsoleKey.LeftArrow,false,true,false)).Read("Title","The Airship");True(result.Back);
        }
        finally{Console.SetOut(previous);}
    }
    static void EveryPromptHasPathContext()
    {
        var f=Fixture();var row=Unresolved(f);var input=new NavigationConsole("","Changed Artist","BACK","","","","Chosen title","1");
        new InteractiveResolution(ManualStore(f),input).Run([row],true,false);
        for(var i=0;i<input.Events.Count;i++)if(input.Events[i].StartsWith("PROMPT:"))
        {
            True(i>0);var context=input.Events[i-1];True(context.EndsWith("\n\n\nFolder: "+Path.GetDirectoryName(row.FullPath))||context.EndsWith("\n\n\nPath: "+row.FullPath));
        }
    }
    static void NavigateBackAcrossFolders()
    {
        var f=Fixture();var store=ManualStore(f);var data=store.Load();
        var first=Unresolved(f) with{FullPath=Path.Combine(f.Source,"Cuphead","55.mp3"),FileName="55.mp3",Track=55,Sha256=new('A',64)};
        var second=first with{FullPath=Path.Combine(f.Source,"Cypress","01.mp3"),FileName="01.mp3",Sha256=new('B',64)};
        data.Folders["folder:a"]=new(){LastKnownPath=Path.GetDirectoryName(first.FullPath)!,Artist="Kristofer Maddigan",Album="Cuphead OST"};
        data.Folders["folder:b"]=new(){LastKnownPath=Path.GetDirectoryName(second.FullPath)!,Artist="Cypress Hill",Album="Till Death Do Us Part"};store.Save(data);
        var input=new NavigationConsole("","Winner Takes All","BACK","The Winner Takes All","BACK","","ESC");
        new InteractiveResolution(store,input).Run([first,second],true,false);
        Equal("The Winner Takes All",store.Load().Files[FileKey(first)].Title);
        var titles=input.Reads.Where(x=>x.Prompt.StartsWith("Title")).ToList();Equal(3,titles.Count);Equal("Winner Takes All",titles[1].Default);Equal("The Winner Takes All",titles[2].Default);
        for(var n=0;n<input.Events.Count;n++)if(input.Events[n].StartsWith("PROMPT:Title"))True(input.Events[n-1].Contains(first.FullPath));
    }
}