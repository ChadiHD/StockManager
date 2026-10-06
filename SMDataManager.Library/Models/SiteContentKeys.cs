using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// The content pages a store can write, with the words the admin screen uses for each (T9).
    /// </summary>
    /// <remarks>
    /// Each key but <c>home</c> is a route on SMStore's <c>ContentPage</c>, declared there as
    /// <c>@page</c> lines because Razor routes are attributes and cannot be generated from a list.
    /// <c>SiteContentKeysTests</c> in SMStore.Tests fails if the two disagree, so a page added to
    /// one is not missing from the other.
    /// </remarks>
    public static class SiteContentKeys
    {
        public const string Home = "home";

        public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
        {
            (Home, "Home page"),
            ("solutions", "Solutions"),
            ("about", "About"),
            ("contact", "Contact"),
            ("terms", "Terms of sale"),
            ("privacy", "Privacy"),
            ("cookies", "Cookies"),
        };

        /// <summary>
        /// What a store must have written before it takes customers: it cannot sell without
        /// terms, and it collects personal data and sets cookies from the first visit.
        /// </summary>
        public static readonly IReadOnlyList<string> RequiredToOpen = new[] { "terms", "privacy", "cookies" };

        public static bool IsKnown(string key) => All.Any(entry => entry.Key == key);
    }
}
