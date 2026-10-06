# Firebase (accounts and cloud saves)

The game signs players in with Firebase Auth and keeps each save in Firestore, all over the REST
APIs from C# (`game/Scripts/Account`). A username is turned into a hidden address,
`name@<project>.firebaseapp.com`, because Firebase Auth only does email logins; no email is ever
sent.

Setup, once, in the Firebase console:

1. Create the project (Analytics off).
2. Authentication → Sign-in method → Email/Password → on.
3. Firestore Database → create (production mode), then paste `firestore.rules` into its Rules tab
   and publish.
4. Project settings → General: the Project ID and Web API key go in
   `game/Scripts/Account/FirebaseConfig.cs` (both are public by design; the rules protect the data).
