using System;
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// Provides search functionality for Shell navigation.
	/// Usage: CometShell.SetSearchHandler(new SearchHandler {
	///     Placeholder = "Search...",
	///     OnQueryChanged = (query) => FilterItems(query)
	/// })
	/// </summary>
	public class SearchHandler
	{
		string _query = string.Empty;

		public string Query
		{
			get => _query;
			set
			{
				if (_query == value)
					return;
				_query = value;
				OnQueryChanged?.Invoke(_query);
			}
		}

		public string Placeholder { get; set; }
		public Action<string> OnQueryChanged { get; set; }
		public Action<object> OnItemSelected { get; set; }
		public Func<string, IEnumerable<object>> ItemsSource { get; set; }
		public bool ShowsResults { get; set; } = true;
		public string CancelButtonText { get; set; }
		public bool IsSearchEnabled { get; set; } = true;

		/// <summary>
		/// Clears the current query and results.
		/// </summary>
		public void ClearQuery()
		{
			Query = string.Empty;
		}

		/// <summary>
		/// Gets the current results based on the query.
		/// </summary>
		public IEnumerable<object> GetResults()
		{
			if (string.IsNullOrEmpty(Query) || ItemsSource is null)
				return Array.Empty<object>();
			return ItemsSource(Query);
		}

		/// <summary>
		/// Selects an item from search results.
		/// </summary>
		public void SelectItem(object item)
		{
			OnItemSelected?.Invoke(item);
		}
	}
}
