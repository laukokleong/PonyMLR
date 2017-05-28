using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;

namespace PonyMLR.Core.DatabaseSelect
{
    public class DatabaseSelectViewModel : ViewModelBase
    {
        private IEventAggregator eventaggregator;

        private ObservableCollection<string> _databases;
        private string _selectedDb;
        private UnitOfWork _uow = null;
        private bool _databaseLocked = false;


        public DatabaseSelectViewModel(IEventAggregator eventaggregator)
        {
            this.eventaggregator = eventaggregator;
         
            this.SetDatabaseCommand = new DelegateCommand<object>(this.SetDatabase, this.CanSetDatabase);
          
            this._databases = new ObservableCollection<string>();
            this._databases.Add("Local");
            this._databases.Add("Remote");
            
            //initialize selected db
            SelectedDb = Databases[0];

            eventaggregator.GetEvent<DatabaseLockedEvent>().Subscribe(OnDatabaseLocked, ThreadOption.UIThread);
            eventaggregator.GetEvent<DatabaseUnlockedEvent>().Subscribe(OnDatabaseUnlocked, ThreadOption.UIThread);
        }
        
        public ObservableCollection<string> Databases
        {
            get
            {
                return this._databases;
            }
        }

        public string SelectedDb
        {
            get
            {
                return this._selectedDb;
            }
            set
            {
                if (this._selectedDb == value) return;
                this._selectedDb = value;
                RaisePropertyChangedEvent("SelectedDb");
            }
        }

        public UnitOfWork Uow
        {
            get
            {
                return _uow;
            }
        }

        private void CommandStateChanged()
        {
            ((DelegateCommand<object>)SetDatabaseCommand).RaiseCanExecuteChanged();
        }

        private void SetDatabase(object arg)
        {
            //set database
            Globals.DbName = SelectedDb;

            // innstantiate uow
            _uow = null;
            _uow = new UnitOfWork(Globals.DbName);

            // inform other modules on database changed
            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Publish(Uow);
            eventaggregator.GetEvent<StatusBarUpdateEvent>().Publish(Globals.DbName + " database selected");
        }

        private bool CanSetDatabase(object arg)
        {
            return !_databaseLocked;
        }

        public ICommand SetDatabaseCommand { get; private set; }

        private void OnDatabaseLocked(string obj)
        {
            _databaseLocked = true;
            CommandStateChanged();
        }

        private void OnDatabaseUnlocked(string obj)
        {
            _databaseLocked = false;
            CommandStateChanged();
        }
    }
}
