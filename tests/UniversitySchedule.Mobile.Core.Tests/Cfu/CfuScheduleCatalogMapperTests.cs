using UniversitySchedule.Contracts.Catalog;
using UniversitySchedule.Mobile.Core.Cfu;

namespace UniversitySchedule.Mobile.Core.Tests.Cfu;

public sealed class CfuScheduleCatalogMapperTests
{
    [Fact]
    public void Map_IgnoresGroupCodeAccidentallyPublishedAsDirection()
    {
        var document = new CfuScheduleIndexDocument
        {
            Tree = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>
            {
                ["Институт экономики и управления"] = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>
                {
                    ["38.03.01 Экономика"] = new Dictionary<string, IReadOnlyList<string>> { ["1"] = ["Э-б-о-261"] },
                    ["РОЭ(ТА) -а-о"] = new Dictionary<string, IReadOnlyList<string>> { ["3"] = ["РОЭ(ТА)-а-о-241"] },
                },
            },
        };

        CfuScheduleCatalog catalog = CfuScheduleCatalogMapper.Map(document);

        DirectionSummary direction = Assert.Single(catalog.Directions);
        Assert.Equal("38.03.01 Экономика", direction.Name);
    }
}
