﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using ABI.System;
using Microsoft.UI;
using Microsoft.Windows.Run.SDK;
using Microsoft.Windows.Run.Sdk.Lib;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace HackerNewsExtension;

public class HackerNewsActionsProvider : IActionProvider
{
    public string DisplayName => $"Hacker News Commands";

    public IconDataType Icon => new(string.Empty);

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable SA1306 // Field names should begin with lower-case letter
    private readonly IListItem[] _Actions = [
#pragma warning restore SA1306 // Field names should begin with lower-case letter
#pragma warning restore IDE1006 // Naming Styles
        new ListItem(new HackerNewsPage()),
    ];

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose() => throw new NotImplementedException();
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize

    public IListItem[] TopLevelActions()
    {
        return _Actions;
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class NewsPost
#pragma warning restore SA1402 // File may only contain a single type
{
    internal string Title { get; init; } = string.Empty;

    internal string Link { get; init; } = string.Empty;

    internal string CommentsLink { get; init; } = string.Empty;

    internal string Poster { get; init; } = string.Empty;
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class LinkAction : InvokableAction
#pragma warning restore SA1402 // File may only contain a single type
{
#pragma warning disable IDE1006 // Naming Styles
    private readonly NewsPost post;
#pragma warning restore IDE1006 // Naming Styles

    internal LinkAction(NewsPost post)
    {
        this.post = post;
        this.Name = "Open link";
        this.Icon = new("\uE8A7");
    }

    public override ActionResult Invoke()
    {
        Process.Start(new ProcessStartInfo(post.Link) { UseShellExecute = true });
        return ActionResult.KeepOpen();
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class CommentAction : InvokableAction
#pragma warning restore SA1402 // File may only contain a single type
{
#pragma warning disable IDE1006 // Naming Styles
    private readonly NewsPost post;
#pragma warning restore IDE1006 // Naming Styles

    internal CommentAction(NewsPost post)
    {
        this.post = post;
        this.Name = "Open comments";
        this.Icon = new("\ue8f2"); // chat bubbles
    }

    public override ActionResult Invoke()
    {
        Process.Start(new ProcessStartInfo(post.CommentsLink) { UseShellExecute = true });
        return ActionResult.KeepOpen();
    }
}

#pragma warning disable SA1400 // Access modifier should be declared
#pragma warning disable SA1402 // File may only contain a single type
sealed class HackerNewsPage : ListPage
#pragma warning restore SA1402 // File may only contain a single type
#pragma warning restore SA1400 // Access modifier should be declared
{
    public HackerNewsPage()
    {
        this.Icon = new("https://news.ycombinator.com/favicon.ico");
        this.Name = "Azure Extension Test";
    }

    private static async Task<List<NewsPost>> GetHackerNewsTopPosts()
    {
        var posts = new List<NewsPost>();

        using (HttpClient client = new HttpClient())
        {
            var response = await client.GetStringAsync("https://news.ycombinator.com/rss");
            var xdoc = XDocument.Parse(response);
            var x = xdoc.Descendants("item").First();
            posts = xdoc.Descendants("item")
                .Take(20)
                .Select(item => new NewsPost()
                {
                    Title = item.Element("title")?.Value ?? string.Empty,
                    Link = item.Element("link")?.Value ?? string.Empty,
                    CommentsLink = item.Element("comments")?.Value ?? string.Empty,
                }).ToList();
        }

        return posts;
    }

    public override ISection[] GetItems()
    {
        var t = DoGetItems();
        t.ConfigureAwait(false);
        return t.Result;
    }

    private async Task<ISection[]> DoGetItems()
    {
        List<NewsPost> items = await GetHackerNewsTopPosts();
        this.Loading = false;
        var s = new ListSection()
        {
            Title = "Posts",
            Items = items.Select((post) => new ListItem(new LinkAction(post))
            {
                Title = post.Title,
                Subtitle = post.Link,
                MoreActions = [
                                new ActionContextItem(new CommentAction(post))
                            ],
            }).ToArray(),
        };
        return [s];
    }
}
