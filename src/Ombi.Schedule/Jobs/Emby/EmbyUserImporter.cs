﻿#region Copyright
// /************************************************************************
//    Copyright (c) 2017 Jamie Rees
//    File: EmbyUserImporter.cs
//    Created By: Jamie Rees
//   
//    Permission is hereby granted, free of charge, to any person obtaining
//    a copy of this software and associated documentation files (the
//    "Software"), to deal in the Software without restriction, including
//    without limitation the rights to use, copy, modify, merge, publish,
//    distribute, sublicense, and/or sell copies of the Software, and to
//    permit persons to whom the Software is furnished to do so, subject to
//    the following conditions:
//   
//    The above copyright notice and this permission notice shall be
//    included in all copies or substantial portions of the Software.
//   
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  ************************************************************************/
#endregion

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ombi.Api.Emby;
using Ombi.Core.Settings;
using Ombi.Core.Settings.Models.External;
using Ombi.Helpers;
using Ombi.Hubs;
using Ombi.Settings.Settings.Models;
using Ombi.Store.Entities;
using Quartz;

namespace Ombi.Schedule.Jobs.Emby
{
    public class EmbyUserImporter : IEmbyUserImporter
    {
        public EmbyUserImporter(IEmbyApi api, UserManager<OmbiUser> um, ILogger<EmbyUserImporter> log,
            ISettingsService<EmbySettings> embySettings, ISettingsService<UserManagementSettings> ums, IHubContext<NotificationHub> notification)
        {
            _api = api;
            _userManager = um;
            _log = log;
            _embySettings = embySettings;
            _userManagementSettings = ums;
            _notification = notification;
        }

        private readonly IEmbyApi _api;
        private readonly UserManager<OmbiUser> _userManager;
        private readonly ILogger<EmbyUserImporter> _log;
        private readonly ISettingsService<EmbySettings> _embySettings;
        private readonly ISettingsService<UserManagementSettings> _userManagementSettings;
        private readonly IHubContext<NotificationHub> _notification;

        public async Task Execute(IJobExecutionContext job)
        {
            var userManagementSettings = await _userManagementSettings.GetSettingsAsync();
            if (!userManagementSettings.ImportEmbyUsers)
            {
                return;
            }
            var settings = await _embySettings.GetSettingsAsync();
            if (!settings.Enable)
            {
                return;
            }

            await _notification.Clients.Clients(NotificationHub.AdminConnectionIds)
                .SendAsync(NotificationHub.NotificationEvent, "Emby User Importer Started");
            var allUsers = await _userManager.Users.Where(x => x.UserType == UserType.EmbyUser).ToListAsync();
            foreach (var server in settings.Servers)
            {
                if (string.IsNullOrEmpty(server.ApiKey))
                {
                    continue;
                }

                var embyUsers = await _api.GetUsers(server.FullUri, server.ApiKey);
                foreach (var embyUser in embyUsers)
                {
                    // Check if we should import this user
                    if (userManagementSettings.BannedEmbyUserIds.Contains(embyUser.Id))
                    {
                        // Do not import these, they are not allowed into the country.
                        continue;
                    }
                    // Check if this Plex User already exists
                    // We are using the Plex USERNAME and Not the TITLE, the Title is for HOME USERS
                    var existingEmbyUser = allUsers.FirstOrDefault(x => x.ProviderUserId == embyUser.Id);
                    if (existingEmbyUser == null)
                    {

                        if (!embyUser.ConnectUserName.HasValue() && !embyUser.Name.HasValue())
                        {
                            _log.LogInformation("Could not create Emby user since the have no username, PlexUserId: {0}", embyUser.Id);
                            continue;
                        }

                        // Create this users
                        // We do not store a password against the user since they will authenticate via Plex
                        var newUser = new OmbiUser
                        {
                            UserType = UserType.EmbyUser,
                            UserName = embyUser.ConnectUserName.HasValue() ? embyUser.ConnectUserName : embyUser.Name,
                            ProviderUserId = embyUser.Id,
                            Alias = string.Empty,
                            EmbyConnectUserId = embyUser.ConnectUserId.HasValue() ? embyUser.ConnectUserId : string.Empty,
                            MovieRequestLimit = userManagementSettings.MovieRequestLimit,
                            EpisodeRequestLimit = userManagementSettings.EpisodeRequestLimit
                        };
                        var result = await _userManager.CreateAsync(newUser);
                        if (!result.Succeeded)
                        {
                            foreach (var identityError in result.Errors)
                            {
                                _log.LogError(LoggingEvents.EmbyUserImporter, identityError.Description);
                            }
                            continue;
                        }
                        if (userManagementSettings.DefaultRoles.Any())
                        {
                            foreach (var defaultRole in userManagementSettings.DefaultRoles)
                            {
                                await _userManager.AddToRoleAsync(newUser, defaultRole);
                            }
                        }
                    }
                    else
                    {
                        // Do we need to update this user?
                        existingEmbyUser.UserName = embyUser.Name;
                        existingEmbyUser.EmbyConnectUserId =
                            embyUser.ConnectUserId.HasValue() ? embyUser.ConnectUserId : string.Empty;

                        if (existingEmbyUser.IsEmbyConnect)
                        {
                            // Note: We do not have access to any of the emby connect details e.g. email
                            // Since we need the username and password to connect to emby connect, 
                            // We update the email address in the OmbiUserManager when the emby connect user logs in
                            existingEmbyUser.UserName = embyUser.ConnectUserName;
                        }

                        await _userManager.UpdateAsync(existingEmbyUser);
                    }
                }
            }

            await _notification.Clients.Clients(NotificationHub.AdminConnectionIds)
                .SendAsync(NotificationHub.NotificationEvent, "Emby User Importer Finished");
        }

        private bool _disposed;
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _userManager?.Dispose();
                _embySettings?.Dispose();
                _userManagementSettings?.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}