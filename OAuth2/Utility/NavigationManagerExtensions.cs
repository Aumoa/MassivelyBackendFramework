using System.Web;
using Microsoft.AspNetCore.Components;

namespace OAuth2.Utility;

public static class NavigationManagerExtensions
{
    /// <summary>
    /// Updates or adds one or more query string parameters in the current URI and navigates to the new URI.
    /// If a parameter already exists, its value is replaced; otherwise, it is added.
    /// The fragment part of the URI is preserved.
    /// </summary>
    /// <param name="nav">The NavigationManager instance.</param>
    /// <param name="nameAndValues">
    /// A set of (name, value) pairs representing query string parameters to update or add.
    /// Each pair is provided as a tuple of string name and string value.
    /// </param>
    public static void NavigateWithQuery(this NavigationManager nav, params ReadOnlySpan<(string, string)> nameAndValues)
    {
        var uri = new Uri(nav.Uri);

        // Parse query string into dictionary
        var query = HttpUtility.ParseQueryString(uri.Query);

        // Update or add parameters from nameAndValues
        foreach (var pair in nameAndValues)
        {
            query[pair.Item1] = pair.Item2;
        }

        // Rebuild query string
        var newQuery = query.ToString();
        if (!string.IsNullOrEmpty(newQuery))
            newQuery = "?" + newQuery;

        // Rebuild fragment
        var fragment = uri.Fragment;
        // fragment includes leading '#', so keep as is

        // Rebuild base uri (scheme://host[:port]/path)
        var baseUri = uri.GetLeftPart(UriPartial.Path);

        // Combine all parts
        var newUri = $"{baseUri}{newQuery}{fragment}";

        nav.NavigateTo(newUri, forceLoad: false);
    }

    /// <summary>
    /// Updates or adds one or more fragment parameters in the current URI and navigates to the new URI.
    /// If a parameter already exists in the fragment, its value is replaced; otherwise, it is added.
    /// The query string part of the URI is preserved.
    /// </summary>
    /// <param name="nav">The NavigationManager instance.</param>
    /// <param name="nameAndValues">
    /// A set of (name, value) pairs representing fragment parameters to update or add.
    /// Each pair is provided as a tuple of string name and string value.
    /// </param>
    public static void NavigateWithFragment(this NavigationManager nav, params ReadOnlySpan<(string, string)> nameAndValues)
    {
        var uri = new Uri(nav.Uri);

        // Parse fragment into dictionary
        var fragment = uri.Fragment;
        // Remove leading '#' if present
        var fragmentContent = fragment.StartsWith("#") ? fragment.Substring(1) : fragment;
        var fragmentParams = HttpUtility.ParseQueryString(fragmentContent);

        // Update or add parameters from nameAndValues
        foreach (var pair in nameAndValues)
        {
            fragmentParams[pair.Item1] = pair.Item2;
        }

        // Rebuild fragment string
        var newFragment = fragmentParams.ToString();
        if (!string.IsNullOrEmpty(newFragment))
            newFragment = "#" + newFragment;

        // Preserve query string
        var query = uri.Query;
        // query includes leading '?', so keep as is

        // Rebuild base uri (scheme://host[:port]/path)
        var baseUri = uri.GetLeftPart(UriPartial.Path);

        // Combine all parts
        var newUri = $"{baseUri}{query}{newFragment}";

        nav.NavigateTo(newUri, forceLoad: false);
    }
}
