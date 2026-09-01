using System.Net;
using System.Text;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Storage;

namespace UniversitySchedule.Mobile.Core.Tests.Profiles;

public sealed class ProfileSetupViewModelTests
{
    [Fact]
    public async Task FirstLaunch_PreselectsPi252FirstSubgroup()
    {
        const string indexJson = """
            {
              "bells": [{"пара": 1, "начало": "08:00", "конец": "09:30"}],
              "weeks": {"ch": ["2026-09-07"], "nch": ["2026-09-14"]},
              "tree": {
                "Физико-технический институт": {
                  "09.03.04 Програмная инженерия": {
                    "2": ["ПИ-б-о-251", "ПИ-б-о-252"]
                  }
                }
              }
            }
            """;
        var store = new MemoryStore();
        var repository = new CfuScheduleRepository(
            new HttpClient(new JsonHandler(indexJson))
            {
                BaseAddress = new Uri(CfuScheduleRepository.BaseAddress),
            },
            store);
        var session = new ScheduleSession(new AcademicProfileStore(store), repository);
        var viewModel = new ProfileSetupViewModel(repository, session);

        await viewModel.InitializeAsync();

        Assert.Equal("Физико-технический институт", viewModel.SelectedInstitute?.Name);
        Assert.Equal("09.03.04 Програмная инженерия", viewModel.SelectedDirection?.Name);
        Assert.Equal(2, viewModel.SelectedCourse);
        Assert.Equal("ПИ-б-о-252", viewModel.SelectedGroup?.Name);
        Assert.Equal(1, viewModel.SelectedSubgroup?.Number);
        Assert.True(viewModel.CanSave);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class MemoryStore : ILocalDataStore
    {
        private readonly Dictionary<string, LocalDocument> _documents = [];

        public Task<LocalDocument?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _documents.TryGetValue(key, out LocalDocument? document);
            return Task.FromResult(document);
        }

        public Task SaveAsync(
            LocalDocument document,
            CancellationToken cancellationToken = default)
        {
            _documents[document.Key] = document;
            return Task.CompletedTask;
        }
    }
}
