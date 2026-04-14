namespace ControlGallery.Models;

public class GithubRelease
{
    public string url { get; set; }
    public string assets_url { get; set; }
    public string upload_url { get; set; }
    public string html_url { get; set; }
    public int id { get; set; }
    public Author author { get; set; }
    public string node_id { get; set; }
    public string tag_name { get; set; }
    public string target_commitish { get; set; }
    public string name { get; set; }
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public DateTime created_at { get; set; }
    public DateTime published_at { get; set; }
    public object[] assets { get; set; }
    public string tarball_url { get; set; }
    public string zipball_url { get; set; }
    public string body { get; set; }
    public Reactions reactions { get; set; }
    public int mentions_count { get; set; }
}

public class Author
{
    public string login { get; set; }
    public int id { get; set; }
    public string node_id { get; set; }
    public string avatar_url { get; set; }
    public string gravatar_id { get; set; }
    public string url { get; set; }
    public string html_url { get; set; }
    public string followers_url { get; set; }
    public string following_url { get; set; }
    public string gists_url { get; set; }
    public string starred_url { get; set; }
    public string subscriptions_url { get; set; }
    public string organizations_url { get; set; }
    public string repos_url { get; set; }
    public string events_url { get; set; }
    public string received_events_url { get; set; }
    public string type { get; set; }
    public bool site_admin { get; set; }
}

public class Reactions
{
    public string url { get; set; }
    public int total_count { get; set; }
    public int _1 { get; set; }
    public int laugh { get; set; }
    public int hooray { get; set; }
    public int confused { get; set; }
    public int heart { get; set; }
    public int rocket { get; set; }
    public int eyes { get; set; }
}