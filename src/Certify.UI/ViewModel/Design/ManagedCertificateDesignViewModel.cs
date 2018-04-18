﻿using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Mock data item view model for use in the XAML designer in Visual Studio 
    /// </summary>
    public class ManagedCertificateDesignViewModel : ViewModel.ManagedCertificateViewModel
    {
        private AppDesignViewModel _appViewModel => (AppDesignViewModel)AppDesignViewModel.Current;

        public ManagedCertificateDesignViewModel()
        {
            // auto-load data if in WPF designer
            bool inDesignMode = !(Application.Current is App);
            if (inDesignMode)
            {
                SelectedItem = _appViewModel.ManagedCertificates.First();

                SelectedItem.RenewalFailureCount = 3;
                SelectedItem.RenewalFailureMessage = "This is an example renewal failure message. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
                SelectedItem.DateLastRenewalAttempt = DateTime.Now.AddMinutes(-30);

                ConfigCheckResults = new System.Collections.ObjectModel.ObservableCollection<StatusMessage> {
                    new StatusMessage{
                        IsOK =true,
                        Message ="This is an example configuration test result."
                    },
                     new StatusMessage{
                        IsOK =false,
                        Message ="This is a failure message."
                    }
                };
            }
            else
            {
                _appViewModel.PropertyChanged += _appViewModel_PropertyChanged;
            }
        }

        private void _appViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_appViewModel.SelectedItem))
            {
                RaisePropertyChangedEvent(nameof(this.SelectedItem));
            }
        }

        protected async override Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            return await Task.Run(() =>
            {
                return Enumerable.Range(1, 50).Select(i => new DomainOption()
                {
                    Domain = $"www{i}.domain.example.org",
                    IsPrimaryDomain = i == 1,
                    IsSelected = true
                });
            });
        }
    }
}