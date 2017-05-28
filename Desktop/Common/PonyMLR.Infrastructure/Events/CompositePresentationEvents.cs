using System.Collections.Generic;
using Microsoft.Practices.Prism.Events;
using PonyMLR.DataAccess;

namespace PonyMLR.Infrastructure
{
    /// <summary>
    /// A composite Presentation event 
    /// </summary>
 
    // Menu bar
    public class NavigationCompletedEvent : CompositePresentationEvent<string> { }
    public class DatabaseSetCompletedEvent : CompositePresentationEvent<object> { }

    // Status bar
    public class StatusBarUpdateEvent : CompositePresentationEvent<string> { }
    public class StatusBarSuppressMessageEvent : CompositePresentationEvent<bool> { }

    // Research panel
    public class ResearchPanelUpdateEvent : CompositePresentationEvent<List<race_result>> { }

    // Modules
    public class DatabaseLockedEvent : CompositePresentationEvent<string> { }
    public class DatabaseUnlockedEvent : CompositePresentationEvent<string> { }
}