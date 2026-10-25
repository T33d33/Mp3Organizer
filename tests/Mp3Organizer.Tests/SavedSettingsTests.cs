using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void SavedSettingsSurviveStaleTerminal()
    {
        string? Read(string name,EnvironmentVariableTarget scope)=>scope==EnvironmentVariableTarget.User?"saved":null;
        if(SavedSettings.Resolve("test",Read,true)!="saved")throw new Exception("Saved user setting not read.");
        if(SavedSettings.Resolve("test",Read,false)!=null)throw new Exception("Non-Windows registry fallback.");
        if(SavedSettings.Resolve("test",(_,scope)=>scope==EnvironmentVariableTarget.Process?"override":"saved",true)!="override")throw new Exception("Process override lost.");
        if(SavedSettings.Resolve("test",(_,scope)=>scope==EnvironmentVariableTarget.Machine?"machine":" ",true)!="machine")throw new Exception("Machine fallback lost.");
        var problem=new IdentificationConfiguration("","",null).Problem??"";
        if(!problem.Contains("ACOUSTID_API_KEY")||!problem.Contains("MP3ORGANIZER_CONTACT")||!problem.Contains("CHROMAPRINT_FPCALC"))throw new Exception("Missing settings must be reported together.");
    }
}
