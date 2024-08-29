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
using DevHomeAzureExtension.Client;
using DevHomeAzureExtension.DataModel;
using DevHomeAzureExtension.DeveloperId;
using DevHomeAzureExtension.Helpers;
using DevHomeAzureExtension.Widgets;
using Microsoft.UI;
using Microsoft.Windows.Run.SDK;
using Microsoft.Windows.Run.Sdk.Lib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Core;
using Windows.Foundation;
using Windows.Storage.Streams;
using YamlDotNet.Serialization;
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
        new ListItem(new QueryFormPage()),
        new ListItem(new OpenAzureQueryPage()),
    ];

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose() => throw new NotImplementedException();
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize

    public IListItem[] TopLevelActions()
    {
        return _Actions;
    }

    internal static string StateJsonPath()
    {
        // Get the path to our exe
        var path = System.Reflection.Assembly.GetExecutingAssembly().Location;

        // Get the directory of the exe
        var directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;

        // now, the state is just next to the exe
        return System.IO.Path.Combine(directory, "state.json");
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class ViewQueryAction : ListPage
#pragma warning restore SA1402 // File may only contain a single type
{
    private readonly string _query;

    internal ViewQueryAction(string query)
    {
        this._query = query;
        this.Name = "View Query";
    }

    private DeveloperId? GetDevId()
    {
        var devIdProvider = DeveloperIdProvider.GetInstance();
        DeveloperId? developerId = null;

        foreach (var devId in devIdProvider.GetLoggedInDeveloperIds().DeveloperIds)
        {
            if (devId.LoginId is not null)
            {
                developerId = (DeveloperId)devId;
            }
        }

        return developerId;
    }

    private string GetIconForType(string? workItemType)
    {
        // For now, just grabbing the icons from githubusercontent, but we should figure this out better in general how to pass pngs around.
        return workItemType switch
        {
            "Bug" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/Bug.png",
            "Feature" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/Feature.png",
            "Issue" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/Issue.png",
            "Impediment" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/Impediment.png",
            "Pull Request" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/PullRequest.png",
            "Task" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/Task.png",
            _ => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/ADO.png",
        };
    }

    private string GetIconForStatusState(string? statusState)
    {
        return statusState switch
        {
            "Closed" or "Completed" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/StatusGreen.png",
            "Committed" or "Resolved" or "Started" => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/StatusBlue.png",
            _ => "https://raw.githubusercontent.com/microsoft/DevHomeAzureExtension/main/src/AzureExtension/Widgets/Assets/StatusGray.png",
        };
    }

    private async Task<ISection[]> DoGetItems()
    {
        try
        {
            var azureDataManager = AzureDataManager.CreateInstance("devpalazuretest") ?? throw new DataStoreInaccessibleException();
            var developerId = GetDevId();
            if (developerId == null)
            {
                // Should not happen, but may be possible in situations where the app is removed and
                // the signed in account is not silently restorable.
                // This is also checked before on UpdateActivityState() method on base class.
                return [new ListSection() { Title = "No queries found", Items = [new ListItem(new NoQueryFoundAction())] }];
            }

            var azureUri = new AzureUri(_query);

            await azureDataManager.UpdateDataForQueryAsync(azureUri, developerId.LoginId);

            if (!azureUri.IsQuery)
            {
                // This should never happen. Already was validated on configuration.
                return [new ListSection() { Title = "No queries found", Items = [new ListItem(new NoQueryFoundAction())] }];
            }

            // This can throw if DataStore is not connected.
            var queryInfo = azureDataManager!.GetQuery(azureUri, developerId.LoginId);

            var queryResults = queryInfo is null ? new Dictionary<string, object>() : JsonConvert.DeserializeObject<Dictionary<string, object>>(queryInfo.QueryResults);

            var itemsData = new JsonObject();
            var itemsArray = new JsonArray();

            foreach (var element in queryResults!)
            {
                var workItem = JsonObject.Parse(element.Value.ToStringInvariant());

                if (workItem != null)
                {
                    // If we can't get the real date, it is better better to show a recent
                    // closer-to-correct time than the zero value decades ago, so use DateTime.UtcNow.
                    var dateTicks = workItem["System.ChangedDate"]?.GetValue<long>() ?? DateTime.UtcNow.Ticks;
                    var dateTime = dateTicks.ToDateTime();
                    var creator = azureDataManager.GetIdentity(workItem["System.CreatedBy"]?.GetValue<long>() ?? 0L);
                    var workItemType = azureDataManager.GetWorkItemType(workItem["System.WorkItemType"]?.GetValue<long>() ?? 0L);
                    var item = new JsonObject
                    {
                        { "title", workItem["System.Title"]?.GetValue<string>() ?? string.Empty },
                        { "url", workItem[AzureDataManager.WorkItemHtmlUrlFieldName]?.GetValue<string>() ?? string.Empty },
                        { "icon", GetIconForType(workItemType.Name) },
                        { "status_icon", GetIconForStatusState(workItem["System.State"]?.GetValue<string>()) },
                        { "number", element.Key },
                        { "user", creator.Name },
                        { "status", workItem["System.State"]?.GetValue<string>() ?? string.Empty },
                        { "avatar", creator.Avatar },
                    };

                    itemsArray.Add(item);
                }
            }

            itemsData.Add("workItemCount", queryInfo is null ? 0 : (int)queryInfo.QueryResultCount);
            itemsData.Add("maxItemsDisplayed", AzureDataManager.QueryResultLimit);
            itemsData.Add("items", itemsArray);

            return [new ListSection()
        {
            Title = "Work Items", Items = itemsArray.Select((workItem) => new ListItem(new OpenQueryAction(workItem?["url"]?.ToString() ?? string.Empty)
            {
                Icon = new IconDataType(workItem?["icon"]?.ToString() ?? string.Empty),
            })
        {
            // get the title from the work item
            Title = workItem?["title"]?.GetValue<string>() ?? string.Empty,
            Subtitle = "!" + workItem?["number"]?.GetValue<string>() ?? string.Empty,
            Tags = [
                new Tag()
                {
                    Text = workItem?["status"]?.GetValue<string>() ?? string.Empty,
                    Color = Windows.UI.Color.FromArgb(255, 0, 0, 255),
                },
            ],
        }).ToArray(),
        }
        ];
        }
        catch
        {
            return [new ListSection() { Title = "No queries found", Items = [new ListItem(new NoQueryFoundAction())] }];
        }
    }

    public override ISection[] GetItems()
    {
        var t = DoGetItems();
        t.ConfigureAwait(false);
        return t.Result;
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
internal sealed class NoQueryFoundAction : InvokableAction
#pragma warning restore SA1402 // File may only contain a single type
{
    internal NoQueryFoundAction()
    {
        this.Name = "No queries found";
        this.Icon = new("\uE8A7");
    }

    public override ActionResult Invoke()
    {
        return ActionResult.GoHome();
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class OpenQueryAction : InvokableAction
#pragma warning restore SA1402 // File may only contain a single type
{
    private readonly string _query;

    internal OpenQueryAction(string query)
    {
        this._query = query;
        this.Name = "Open Query";
        this.Icon = new("\uE8A7");
    }

    public override ActionResult Invoke()
    {
        Process.Start(new ProcessStartInfo(_query) { UseShellExecute = true });
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

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class QueryFormPage : FormPage
#pragma warning restore SA1402 // File may only contain a single type
{
    internal event TypedEventHandler<object, object?>? AddedQuery;

    public QueryFormPage()
    {
        this.Name = "Add Azure Query";
        this.Icon = new("https://www.c-sharpcorner.com/UploadFile/BlogImages/01232023170209PM/Azure%20Icon.png");
    }

    public override string TemplateJson()
    {
        var json = $$"""
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.5",
  "body": [
    {
      "type": "Container",
      "items": [
        {
          "type": "Input.Text",
          "id": "query",
          "label": "Enter your ADO Query URL",
          "style": "Url",
          "isRequired": true,
          "errorMessage": "Query is required"
        },
        {
          "type": "Input.Text",
          "id": "name",
          "label": "Query Name",
          "isRequired": true,
          "errorMessage": "Name is required"
        },
        {
          "type": "Container",
          "$when": "${message != null}",
          "items": [
            {
              "type": "TextBlock",
              "text": "${message}",
              "wrap": true,
              "size": "small"
            }
          ],
          "style": "warning"
        },
        {
          "type": "ColumnSet",
          "spacing": "Medium",
          "columns": [
            {
              "type": "Column",
              "width": "stretch"
            },
            {
              "type": "Column",
              "width": "auto",
              "items": [
                {
                  "type": "Container",
                  "items": [
                    {
                      "type": "ActionSet",
                      "actions": [
                        {
                          "type": "Action.Submit",
                          "title": "Save",
                          "data": {
                              "query": "query",
                              "name": "name"
                        }
                        }
                      ]
                    }
                  ]
                }
              ]
            },
            {
              "type": "Column",
              "width": "stretch"
            }
          ]
        }
      ]
    }
  ]
}
""";
        return json;
    }

    public override string DataJson() => throw new NotImplementedException();

    public override string StateJson() => throw new NotImplementedException();

    public override IActionResult SubmitForm(string payload)
    {
        var formInput = JsonObject.Parse(payload);
        if (formInput == null)
        {
            throw new ArgumentNullException(nameof(payload), "Form input cannot be null");
        }

        var query = formInput["query"]?.GetValue<string>() ?? string.Empty;
        var name = formInput["name"]?.GetValue<string>() ?? string.Empty;

        var json = string.Empty;

        // if the file exists, load it and append the new item
        if (System.IO.File.Exists(CommandPaletteActionsProvider.StateJsonPath()))
        {
            var state = System.IO.File.ReadAllText(CommandPaletteActionsProvider.StateJsonPath());
            var jsonState = JObject.Parse(state);
            if (jsonState.ContainsKey("items"))
            {
                var items = jsonState["items"];
                var newItem = new JObject
                {
                    { "query", query },
                    { "name", name },
                };
                items?.Last?.AddAfterSelf(newItem);
                json = jsonState.ToString();
            }
        }
        else
        {
            json = $$"""
{
    "items": [
        {
            "query": "{{query}}",
            "name": "{{name}}",
            "test": "testing"
        }
        ]
}
""";
        }

        System.IO.File.WriteAllText(CommandPaletteActionsProvider.StateJsonPath(), json);
        AddedQuery?.Invoke(this, null);
        return ActionResult.GoHome();
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal sealed class OpenAzureQueryPage : ListPage
#pragma warning restore SA1402 // File may only contain a single type
{
    public OpenAzureQueryPage()
    {
        this.Icon = new("https://www.c-sharpcorner.com/UploadFile/BlogImages/01232023170209PM/Azure%20Icon.png");
        this.Name = "My Azure Queries";
    }

    private ListItem NoSavedQueries()
    {
        return new ListItem(new NoQueryFoundAction())
        {
            Title = "No saved queries",
            Subtitle = "You can add queries from the form page",
        };
    }

    public override ISection[] GetItems()
    {
        var items = new List<IListItem>();
        try
        {
            if (System.IO.File.Exists(CommandPaletteActionsProvider.StateJsonPath()))
            {
                var state = System.IO.File.ReadAllText(CommandPaletteActionsProvider.StateJsonPath());
                var jsonState = JObject.Parse(state);
                if (jsonState.ContainsKey("items"))
                {
                    var itemsArray = jsonState["items"];
                    if (itemsArray is null)
                    {
                        items.Add(NoSavedQueries());
                    }
                    else
                    {
                        foreach (var item in itemsArray)
                        {
                            var query = item["query"]?.ToString() ?? string.Empty;
                            var name = item["name"]?.ToString() ?? string.Empty;
                            items.Add(new ListItem(new OpenQueryAction(query))
                            {
                                Title = name,
                                Subtitle = query,
                                MoreActions = [
                                    new ActionContextItem(new ViewQueryAction(query))
                                    ],
                            });
                        }
                    }
                }
                else
                {
                    items.Add(NoSavedQueries());
                }

                return [new ListSection() { Title = "Saved Queries", Items = items.ToArray() }];
            }
        }
        catch (System.Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        items.Add(NoSavedQueries());
        return [new ListSection() { Title = "Saved Queries", Items = items.ToArray() }];
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

    private string GetIconForPullRequestStatus(string prStatus)
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
#pragma warning disable CS8604 // Possible null reference argument.
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
#pragma warning restore CS8604 // Possible null reference argument.

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
