﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class APIResult
    {
        public bool IsOK { get; set; }
        public string Message { get; set; }
        public List<string> FailedItemSummary { get; set; }
        public object Result { get; set; }

        public APIResult()
        {
            IsOK = true;
            FailedItemSummary = new List<string>();
        }
    }
}