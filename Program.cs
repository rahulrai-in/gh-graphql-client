using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;

namespace RepoMonitor
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var repositoriesToInspect = new List<string> { "gh-graphql-client" };
            Console.Write("Enter GitHub Personal Access Token and press enter: ");
            var token = Console.ReadLine();
            Console.WriteLine("\n");
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            var gqlOptions = new GraphQLHttpClientOptions { EndPoint = new Uri("https://api.github.com/graphql") };
            var gqlClient = new GraphQLHttpClient(gqlOptions, new SystemTextJsonSerializer(), httpClient);

            foreach (var repository in repositoriesToInspect)
            {
                Console.WriteLine("Inspecting repo: {0}", repository);
                var openIssues = await GetOpenIssuesInRepoPastThreshold(repository) ?? new List<Node>();
                Console.WriteLine(" Found {0} stale issues", openIssues.Count);
                foreach (var openIssue in openIssues)
                {
                    Console.WriteLine("     Posting comment on issue {0}", openIssue.Url);
                    var comment = await CommentOnIssue(openIssue.Id);
                    Console.WriteLine("     Comment posted here: {0}", comment.Url);
                }

                Console.WriteLine("Finished processing issues in repository {0}", repository);
            }

            Console.WriteLine("Done. Bye!");
            Console.ReadKey();

            // Query
            async Task<List<Node>> GetOpenIssuesInRepoPastThreshold(string repository)
            {
                var issueStalenessFilter = DateTime.UtcNow - TimeSpan.FromHours(12);
                var request = new GraphQLRequest
                {
                    Query =
                        @"query GetOpenIssuesPastThreshold($repositoryName: String!, $repositoryOwner: String!, $issuesStates: [IssueState!], $issuesFirst: Int) {
                              repository(name: $repositoryName, owner: $repositoryOwner) {
                                issues(states: $issuesStates, first: $issuesFirst) {
                                  nodes {
                                    id
                                    updatedAt
                                    url
                                  }
                                }
                              }
                        }",
                    Variables = new
                    {
                        repositoryName = repository,
                        repositoryOwner = "rahulrai-in",
                        issuesStates = "OPEN",
                        issuesFirst = 100
                    }
                };

                var response = await gqlClient.SendQueryAsync<Root>(request);
                return response?.Data?.Repository?.Issues?.Nodes.Where(n => n.UpdatedAt < issueStalenessFilter)
                    .ToList();
            }

            // Mutation
            async Task<Node> CommentOnIssue(string openIssueId)
            {
                var request = new GraphQLRequest
                {
                    Query =
                        @"mutation AddCommentMutation($addCommentInput: AddCommentInput!) {
                              addComment(input: $addCommentInput) {
                                commentEdge {
                                  node {
                                    url
                                  }
                                }
                              }
                        }",
                    Variables = new
                    {
                        addCommentInput = new
                        {
                            body =
                                "This issue has breached the Stale Issue policy. Please close this issue or update this conversation to inform the parties about the latest status of the fix.",
                            subjectId = openIssueId
                        }
                    }
                };

                var response = await gqlClient.SendQueryAsync<Root>(request);
                return response?.Data?.AddComment?.CommentEdge?.Node;
            }
        }
    }

    public class Node
    {
        public string Id { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Url { get; set; }
    }

    public class Issues
    {
        public List<Node> Nodes { get; set; }
    }

    public class Repository
    {
        public Issues Issues { get; set; }
    }

    public class CommentEdge
    {
        public Node Node { get; set; }
    }

    public class AddComment
    {
        public CommentEdge CommentEdge { get; set; }
    }

    public class Root
    {
        public Repository Repository { get; set; }
        public AddComment AddComment { get; set; }
    }
}