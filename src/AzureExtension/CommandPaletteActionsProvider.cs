// Copyright (c) Microsoft Corporation.
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using ABI.System;
using DevHomeAzureExtension;
using DevHomeAzureExtension.DataModel;
using DevHomeAzureExtension.Helpers;
using DevHomeAzureExtension.Widgets;
using Microsoft.UI;
using Microsoft.Windows.Run.SDK;
using Microsoft.Windows.Run.Sdk.Lib;
using Newtonsoft.Json;
using Serilog.Core;
using Windows.Foundation;
using Windows.Storage.Streams;
using static System.Net.WebRequestMethods;

namespace AzureCommandPaletteExtension;

public class CommandPaletteActionsProvider : IActionProvider
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
internal sealed class OpenPRAction : InvokableAction
#pragma warning restore SA1402 // File may only contain a single type
{
#pragma warning disable IDE1006 // Naming Styles
    private readonly JsonNode pullRequest;
#pragma warning restore IDE1006 // Naming Styles

    internal OpenPRAction(JsonNode pullRequest)
    {
        this.pullRequest = pullRequest;
        this.Name = "Open PR";
        this.Icon = new(pullRequest?["status_icon"]?.GetValue<string>() ?? string.Empty);
    }

    public override ActionResult Invoke()
    {
        Process.Start(new ProcessStartInfo(pullRequest?["url"]?.GetValue<string>() ?? string.Empty) { UseShellExecute = true });
        return ActionResult.Dismiss();
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
        this.Icon = new("https://www.c-sharpcorner.com/UploadFile/BlogImages/01232023170209PM/Azure%20Icon.png");
        this.Name = "My Pull Requests";
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

    private string GetIconForPullRequestStatus(string? prStatus)
    {
        prStatus ??= string.Empty;
        if (Enum.TryParse<PolicyStatus>(prStatus, false, out var policyStatus))
        {
            return policyStatus switch
            {
                PolicyStatus.Approved => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestApproved.png",
                PolicyStatus.Running => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestWaiting.png",
                PolicyStatus.Queued => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestWaiting.png",
                PolicyStatus.Rejected => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestRejected.png",
                PolicyStatus.Broken => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestRejected.png",
                _ => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/01897def84df3fbaa4af22ce596fa9555a3293d5/src/AzureExtension/Widgets/Assets/PullRequestReviewNotStarted.png",
            };
        }

        return string.Empty;
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
        var azureDataManager = AzureDataManager.CreateInstance("devpalazuretest") ?? throw new DataStoreInaccessibleException();
        var pullRequestsList = azureDataManager.GetPullRequestsForLoggedInDeveloperIds();
        this.Loading = false;
        var itemsData = new JsonObject();
        var itemsArray = new JsonArray();
        foreach (var pullRequests in pullRequestsList.Where(x => x is not null))
        {
            var pullRequestsResults = JsonConvert.DeserializeObject<Dictionary<string, object>>(pullRequests.Results);
            if (pullRequestsResults is null)
            {
                continue;
            }

            foreach (var element in pullRequestsResults)
            {
                var workItem = JsonObject.Parse(element.Value.ToStringInvariant());

                if (workItem != null)
                {
                    // If we can't get the real date, it is better better to show a recent
                    // closer-to-correct time than the zero value decades ago, so use DateTime.UtcNow.
                    var dateTicks = workItem["CreationDate"]?.GetValue<long>() ?? DateTime.UtcNow.Ticks;
                    var dateTime = dateTicks.ToDateTime();
                    var creator = azureDataManager.GetIdentity(workItem["CreatedBy"]?.GetValue<long>() ?? 0L);
                    var item = new JsonObject
                        {
                            { "title", workItem["Title"]?.GetValue<string>() ?? string.Empty },
                            { "url", workItem["HtmlUrl"]?.GetValue<string>() ?? string.Empty },
                            { "status_icon", GetIconForPullRequestStatus(workItem["PolicyStatus"]?.GetValue<string>()) },
                            { "number", element.Key },
                            { "repository", pullRequests.Repository.Name ?? string.Empty },
                            { "dateTicks", dateTicks },
                            { "user", creator.Name },
                            { "branch", workItem["TargetBranch"]?.GetValue<string>().Replace("refs/heads/", string.Empty) },
                            { "avatar", creator.Avatar },
                        };

                    itemsArray.Add(item);
                }
            }
        }

        // Sort all pull requests by creation date, descending so newer are at the top of the list.
        var sortedItems = itemsArray.Where(x => x is not null).OrderByDescending(x => x?["dateTicks"]?.GetValue<long>());
        var sortedItemsArray = new JsonArray();
        foreach (var item in sortedItems)
        {
            // Parent is read-only and fixed, use deep clone to re-parent the node.
            var itemClone = item?.DeepClone();
            sortedItemsArray.Add(itemClone);
        }

        itemsData.Add("maxItemsDisplayed", AzureDataManager.PullRequestResultLimit);
        itemsData.Add("items", sortedItemsArray);

#pragma warning disable CS8604 // Possible null reference argument.
        var s = new ListSection()
        {
            Title = "Pull Requests",
            Items = itemsArray.Select((pullRequest) => new ListItem(new OpenPRAction(pullRequest))
            {
                // get the title from the pr
                Title = pullRequest?["title"]?.GetValue<string>() ?? string.Empty,
                Subtitle = "!" + pullRequest?["number"]?.GetValue<string>() ?? string.Empty,
                Tags = [
                                    new Tag()
                                {
                                    Text = pullRequest?["repository"]?.GetValue<string>() ?? string.Empty,
                                    Color = Windows.UI.Color.FromArgb(255, 0, 0, 255),
                                },
                            ],
            }).ToArray(),
        };
#pragma warning restore CS8604 // Possible null reference argument.
        return [s];
    }
}
