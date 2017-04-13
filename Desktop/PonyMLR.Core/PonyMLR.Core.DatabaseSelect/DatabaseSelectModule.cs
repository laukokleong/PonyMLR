using System;
using System.Collections.Generic;
using Microsoft.Practices.Prism.Modularity;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using Microsoft.Practices.Unity;
using PonyMLR.Infrastructure;

namespace PonyMLR.Core.DatabaseSelect
{
    public class DatabaseSelectModule : IModule
    {
        /// <summary>
        /// Initializes the module.
        /// </summary>
        public void Initialize()
        {
            // Register database select panel with Prism Region
            var regionManager = ServiceLocator.Current.GetInstance<IRegionManager>();
            regionManager.RegisterViewWithRegion(RegionNames.MenuRegion, typeof(DatabaseSelectView));

        }
    }
}
