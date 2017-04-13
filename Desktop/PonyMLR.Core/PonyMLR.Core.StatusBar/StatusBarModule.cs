using System;
using System.Collections.Generic;
using Microsoft.Practices.Prism.Modularity;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using Microsoft.Practices.Unity;
using PonyMLR.Infrastructure;

namespace PonyMLR.Core.StatusBar
{
    public class StatusBarModule : IModule
    {
        /// <summary>
        /// Initializes the module.
        /// </summary>
        public void Initialize()
        {
            // Register status bar with Prism Region
            var regionManager = ServiceLocator.Current.GetInstance<IRegionManager>();
            regionManager.RegisterViewWithRegion(RegionNames.StatusRegion, typeof(StatusBarView));

        }
    }
}
