﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models.Shared
{
    public class ActionLogCollector
    {
        protected List<ActionLogItem> _actionLogs { get; }

        public ActionLogCollector()
        {
            _actionLogs = new List<ActionLogItem>();
            _actionLogs.Capacity = 1000;
        }

        protected void LogAction(string command, string result = null, string managedSiteId = null)
        {
            if (this._actionLogs != null)
            {
                _actionLogs.Add(new ActionLogItem
                {
                    Command = command,
                    Result = result,
                    ManagedSiteId = managedSiteId,
                    DateTime = DateTime.Now
                });
            }
        }

        public List<string> GetActionLogSummary()
        {
            List<string> output = new List<string>();
            if (_actionLogs != null)
            {
                _actionLogs.ToList().ForEach((a) =>
                {
                    output.Add(a.Command + " : " + (a.Result != null ? a.Result : ""));
                });
            }

            return output;
        }

        public ActionLogItem GetLastActionLogItem()
        {
            return _actionLogs.LastOrDefault();
        }
    }
}