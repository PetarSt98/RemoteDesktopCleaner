﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public interface ISynchronizer
    {
        Task SynchronizeAsync(string serverName);
    }
}
