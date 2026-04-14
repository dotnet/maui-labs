using System;
using System.Net.Http;
using CodeHollow.FeedReader;
using AsyncAwaitBestPractices;
using System.Diagnostics;
using AngleSharp.Html.Parser;
using System.Text.Json;
using ControlGallery.Models;
using System.Reflection;

namespace ControlGallery.Pages;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    private List<FeedItem> articles;

    [ObservableProperty]
    private GithubRelease latestRelease;

    [ObservableProperty]
    private string _mauiVersion;

    [RelayCommand]
    async Task NavigateTo(string link)
    {
        try
        {
            await Browser.OpenAsync(link);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Crap: {ex.Message}");
        }
    }

	public HomeViewModel()
    {
		// LoadBlogs().SafeFireAndForget();
        // GetLatestRelease().SafeFireAndForget();
        MauiVersion = GetMauiVersion(); 

	}

    async Task LoadBlogs()
    {
        Feed feed = null;
        try
        {
            feed = await FeedReader.ReadAsync("https://devblogs.microsoft.com/dotnet/category/maui/feed/");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{ex.Message}");
        }

        var htmlParser = new HtmlParser();
        foreach (var item in feed?.Items)
        {

            var document = htmlParser.ParseDocument(item.Description);
            item.Description = document.QuerySelector("p").TextContent;
        }

        try
        {
            Articles = new List<FeedItem>(feed?.Items);
        }catch(Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    HttpClient _client;
    JsonSerializerOptions _serializerOptions;

    async Task GetLatestRelease()
    {
        _client = new HttpClient();
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        Uri uri = new Uri("https://api.github.com/repos/dotnet/maui/releases/latest");
        try
        {
            HttpResponseMessage response = await _client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                LatestRelease = JsonSerializer.Deserialize<GithubRelease>(content, _serializerOptions);
                //Items = JsonSerializer.Deserialize<List<TodoItem>>(content, _serializerOptions);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
        }

    }

    string GetMauiVersion()
    {
        var attr = typeof(MauiApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string version = attr.InformationalVersion;
        return version;
    }
}