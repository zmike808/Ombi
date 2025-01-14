﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ombi.Core.Notifications;
using Ombi.Helpers;
using Ombi.Hubs;
using Ombi.Notifications.Models;
using Ombi.Store.Entities;
using Ombi.Store.Entities.Requests;
using Ombi.Store.Repository;
using Ombi.Store.Repository.Requests;
using Quartz;

namespace Ombi.Schedule.Jobs.Plex
{
    public class PlexAvailabilityChecker : IPlexAvailabilityChecker
    {
        public PlexAvailabilityChecker(IPlexContentRepository repo, ITvRequestRepository tvRequest, IMovieRequestRepository movies,
            INotificationService notification, IBackgroundJobClient background, ILogger<PlexAvailabilityChecker> log, IHubContext<NotificationHub> hub)
        {
            _tvRepo = tvRequest;
            _repo = repo;
            _movieRepo = movies;
            _notificationService = notification;
            _backgroundJobClient = background;
            _log = log;
            _notification = hub;
        }

        private readonly ITvRequestRepository _tvRepo;
        private readonly IMovieRequestRepository _movieRepo;
        private readonly IPlexContentRepository _repo;
        private readonly INotificationService _notificationService;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger _log;
        private readonly IHubContext<NotificationHub> _notification;

        public async Task Execute(IJobExecutionContext job)
        {
            try
            {

                await _notification.Clients.Clients(NotificationHub.AdminConnectionIds)
                    .SendAsync(NotificationHub.NotificationEvent, "Plex Availability Check Started");
                await ProcessMovies();
                await ProcessTv();
            }
            catch (Exception e)
            {
                await _notification.Clients.Clients(NotificationHub.AdminConnectionIds)
                    .SendAsync(NotificationHub.NotificationEvent, "Plex Availability Check Failed");
                _log.LogError(e, "Exception thrown in Plex availbility checker");
                return;
            }

            await _notification.Clients.Clients(NotificationHub.AdminConnectionIds)
                .SendAsync(NotificationHub.NotificationEvent, "Plex Availability Check Finished");
        }

        private Task ProcessTv()
        {
            var tv = _tvRepo.GetChild().Where(x => !x.Available);
            return ProcessTv(tv);
        }

        private async Task ProcessTv(IQueryable<ChildRequests> tv)
        {
            var plexEpisodes = _repo.GetAllEpisodes().Include(x => x.Series);

            foreach (var child in tv)
            {
                var useImdb = false;
                var useTvDb = false;
                if (child.ParentRequest.ImdbId.HasValue())
                {
                    useImdb = true;
                }

                if (child.ParentRequest.TvDbId.ToString().HasValue())
                {
                    useTvDb = true;
                }

                var tvDbId = child.ParentRequest.TvDbId;
                var imdbId = child.ParentRequest.ImdbId;
                IQueryable<PlexEpisode> seriesEpisodes = null;
                if (useImdb)
                {
                    seriesEpisodes = plexEpisodes.Where(x => x.Series.ImdbId == imdbId.ToString());
                }
                if (useTvDb && (seriesEpisodes == null || !seriesEpisodes.Any()))
                {
                    seriesEpisodes = plexEpisodes.Where(x => x.Series.TvDbId == tvDbId.ToString());
                }

                if (seriesEpisodes == null)
                {
                    continue;
                }

                if (!seriesEpisodes.Any())
                {
                    // Let's try and match the series by name
                    seriesEpisodes = plexEpisodes.Where(x =>
                        x.Series.Title.Equals(child.Title, StringComparison.InvariantCultureIgnoreCase) &&
                        x.Series.ReleaseYear.Equals(child.ParentRequest.ReleaseDate.Year.ToString(), StringComparison.InvariantCultureIgnoreCase));

                }

                foreach (var season in child.SeasonRequests)
                {
                    foreach (var episode in season.Episodes)
                    {
                        if (episode.Available)
                        {
                            continue;
                        }
                        var foundEp = await seriesEpisodes.FirstOrDefaultAsync(
                            x => x.EpisodeNumber == episode.EpisodeNumber &&
                                 x.SeasonNumber == episode.Season.SeasonNumber);

                        if (foundEp != null)
                        {
                            episode.Available = true;
                        }
                    }
                }

                // Check to see if all of the episodes in all seasons are available for this request
                var allAvailable = child.SeasonRequests.All(x => x.Episodes.All(c => c.Available));
                if (allAvailable)
                {
                    _log.LogInformation("[PAC] - Child request {0} is now available, sending notification", $"{child.Title} - {child.Id}");
                    // We have ful-fulled this request!
                    child.Available = true;
                    child.MarkedAsAvailable = DateTime.Now;
                    await _notificationService.Publish(new NotificationOptions
                    {
                        DateTime = DateTime.Now,
                        NotificationType = NotificationType.RequestAvailable,
                        RequestId = child.Id,
                        RequestType = RequestType.TvShow,
                        Recipient = child.RequestedUser.Email
                    });
                }
            }

            await _tvRepo.Save();
        }

        private async Task ProcessMovies()
        {
            // Get all non available
            var movies = _movieRepo.GetAll().Include(x => x.RequestedUser).Where(x => !x.Available);

            foreach (var movie in movies)
            {
                PlexServerContent item = null;
                if (movie.ImdbId.HasValue())
                {
                    item = await _repo.Get(movie.ImdbId);
                }
                if (item == null)
                {
                    if (movie.TheMovieDbId.ToString().HasValue())
                    {
                        item = await _repo.Get(movie.TheMovieDbId.ToString());
                    }
                }
                if (item == null)
                {
                    // We don't yet have this
                    continue;
                }

                movie.Available = true;
                movie.MarkedAsAvailable = DateTime.Now;
                item.RequestId = movie.Id;

                _log.LogInformation("[PAC] - Movie request {0} is now available, sending notification", $"{movie.Title} - {movie.Id}");
                await _notificationService.Publish(new NotificationOptions
                {
                    DateTime = DateTime.Now,
                    NotificationType = NotificationType.RequestAvailable,
                    RequestId = movie.Id,
                    RequestType = RequestType.Movie,
                    Recipient = movie.RequestedUser != null ? movie.RequestedUser.Email : string.Empty
                });

            }

            await _movieRepo.Save();
            await _repo.SaveChangesAsync();
        }

        private bool _disposed;
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _movieRepo?.Dispose();
                _repo?.Dispose();
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