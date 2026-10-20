using System;
using System.Collections;
using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace NeonHunger
{
    /// <summary>
    /// The public scoreboard (neon-scores.php on hosthobbit.com). A submission carries the run's
    /// per-level counts rather than just a number: the server recomputes the score with the same
    /// point values and checks every count against what each level actually contains.
    /// </summary>
    public static class Scoreboard
    {
        public const string Url = "https://hosthobbit.com/games-api/neon-scores.php";
        public const int Version = 1;

        [Serializable] public class Entry { public string name; public int score; public int level; public bool won; public string date; }
        [Serializable] class TopList { public Entry[] scores; }
        [Serializable] class Reply { public bool ok; public int rank; public string error; }

        public static IEnumerator Fetch(Action<List<Entry>, string> done)
        {
            using (var req = UnityWebRequest.Get(Url + "?top=20"))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { done(null, "Couldn't reach the scoreboard. Check your connection."); yield break; }
                try { done(new List<Entry>(JsonUtility.FromJson<TopList>(req.downloadHandler.text).scores ?? new Entry[0]), null); }
                catch { done(null, "The scoreboard sent something odd back."); }
            }
        }

        public static IEnumerator Submit(string name, RunScore run, Action<bool, string> done)
        {
            var form = new WWWForm();
            form.AddField("name", name);
            form.AddField("levels", run.Encode());
            form.AddField("score", run.Total);
            form.AddField("v", Version);
            using (var req = UnityWebRequest.Post(Url, form))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                Reply r = null;
                try { r = JsonUtility.FromJson<Reply>(req.downloadHandler.text); } catch { }
                if (r != null && r.ok) done(true, $"You're number {r.rank} on the scoreboard!");
                else if (r != null && !string.IsNullOrEmpty(r.error)) done(false, r.error);
                else done(false, "Couldn't reach the scoreboard. Check your connection.");
            }
        }
    }
}
