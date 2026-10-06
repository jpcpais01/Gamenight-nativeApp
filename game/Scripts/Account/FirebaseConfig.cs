namespace GameNight.Account;

/// <summary>The Firebase project behind accounts (console.firebase.google.com). Both values are
/// public by design: the API key only names the project, and the Firestore rules in
/// firestore.rules decide who may read what. Empty means accounts aren't switched on yet.</summary>
public static class FirebaseConfig
{
    public const string BuiltKey = "AIzaSyCEFidsWO0R8CHac1WFC7i1b8Hw8sW0sOk";
    public const string BuiltProject = "gamenight-68aa3";

    /// <summary>Debug: `-- --firebase=apiKey@projectId` talks to another project.</summary>
    static string[] Override
    {
        get
        {
            foreach (var a in Godot.OS.GetCmdlineUserArgs())
                if (a.StartsWith("--firebase=") && a.Contains('@')) return a[11..].Split('@', 2);
            return null;
        }
    }

    public static string ApiKey => Override?[0] ?? BuiltKey;
    public static string ProjectId => Override?[1] ?? BuiltProject;
    public static bool Ready => ApiKey.Length > 0 && ProjectId.Length > 0;
}
