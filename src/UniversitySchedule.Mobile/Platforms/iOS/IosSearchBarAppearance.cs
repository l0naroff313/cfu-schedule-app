using Microsoft.Maui.Handlers;
using UIKit;

namespace UniversitySchedule.Mobile;

internal static class IosSearchBarAppearance
{
    public static void Configure()
    {
        SearchBarHandler.Mapper.AppendToMapping(
            nameof(IosSearchBarAppearance),
            static (handler, _) =>
            {
                UISearchBar searchBar = handler.PlatformView;
                searchBar.SearchBarStyle = UISearchBarStyle.Minimal;
                searchBar.BackgroundImage = new UIImage();
                searchBar.BackgroundColor = UIColor.Clear;
                searchBar.BarTintColor = UIColor.Clear;
                searchBar.SearchTextField.BackgroundColor = UIColor.Clear;
                searchBar.SearchTextField.BorderStyle = UITextBorderStyle.None;
            });
    }
}
