using Mp3Organizer;

namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void DiagnosticOnlineConditions()
    {
        var config=new IdentificationConfiguration("secret","test@example.invalid","fake.exe");
        Equal("Online lookup enabled",config.Status(true,false));True(config.Status(false,false).Contains("--online not specified"));True(config.Status(false,true).Contains("--offline requested"));
        True((config with{ApiKey=""}).Status(true,false).Contains("AcoustID API key not configured"));
        True((config with{Contact=""}).Status(true,false).Contains("MusicBrainz contact not configured"));
        True((config with{Fpcalc=null}).Status(true,false).Contains("fpcalc executable not found"));
    }
    static void DiagnosticFpcalcLocation()
    {
        var folder=Path.Combine(root,"fake-bin");Directory.CreateDirectory(folder);var executable=Path.Combine(folder,"fpcalc.exe");File.WriteAllText(executable,"not executed");
        Equal(executable,IdentificationConfiguration.FindFpcalc(null,folder));Equal(executable,IdentificationConfiguration.FindFpcalc(executable,""));
        Equal<string?>(null,IdentificationConfiguration.FindFpcalc(Path.Combine(folder,"missing.exe"),folder));
    }
    static void DoctorChecksWorkspaceOnly()
    {
        var f=Fixture();var before=Snapshot(f.Source);var workspace=Path.Combine(f.Reports,"doctor");var calls=0;
        var config=new IdentificationConfiguration("TOP-SECRET","test@example.invalid","fake.exe");
        var previous=Console.Out;using var output=new StringWriter();Console.SetOut(output);
        try
        {
            Equal(0,IdentificationDoctor.Run(workspace,null,false,config,_=>Task.FromResult("fpcalc version fake"),_=>{calls++;return Task.FromResult("reachable (mock)");}));
            Equal(1,calls);Equal(0,IdentificationDoctor.Run(workspace,null,true,config,_=>Task.FromResult("version fake"),_=>throw new Exception("Network forbidden")));
            File.WriteAllText(Path.Combine(workspace,"manual-resolutions.json"),"{broken");
            Equal(1,IdentificationDoctor.Run(workspace,null,true,config,_=>throw new IOException("version failed")));
        }
        finally{Console.SetOut(previous);}
        True(output.ToString().Contains("Manual resolutions: FAILED"));True(!output.ToString().Contains("TOP-SECRET"));Equal(before,Snapshot(f.Source));
        using var db=new SqliteDatabase(Path.Combine(workspace,"cache.db"));Equal("0",db.Query("SELECT count(*) FROM cache_entries")[0][0]);
    }
    static void DoctorFailuresReported()
    {
        var f=Fixture();var workspace=Path.Combine(f.Reports,"doctor");var config=new IdentificationConfiguration("secret","test@example.invalid","fake.exe");
        var previous=Console.Out;using var output=new StringWriter();Console.SetOut(output);
        try
        {
            Equal(1,IdentificationDoctor.Run(workspace,f.File,true,config,_=>Task.FromResult("version fake")));
            Equal(1,IdentificationDoctor.Run(workspace,null,false,config,_=>Task.FromResult("version fake"),_=>throw new IOException("HTTP 503 secret")));
        }
        finally{Console.SetOut(previous);}
        True(output.ToString().Contains("Cache database: FAILED"));True(output.ToString().Contains("MusicBrainz connectivity: FAILED"));True(!output.ToString().Contains("secret"));
    }
}
