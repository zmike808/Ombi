﻿using AutoMapper;

using Ombi.Api.Trakt;
using Ombi.Api.TvMaze;
using Ombi.Core.Engine.Interfaces;
using Ombi.Core.Models.Requests;
using Ombi.Core.Models.Search;
using Ombi.Core.Settings;
using Ombi.Core.Settings.Models.External;
using Ombi.Store.Repository;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Ombi.Core.Rule.Interfaces;
using Ombi.Store.Repository.Requests;
using Microsoft.Extensions.Caching.Memory;
using Ombi.Core.Authentication;
using Ombi.Helpers;
using Ombi.Settings.Settings.Models;
using Ombi.Store.Entities;
using TraktSharp.Entities;

namespace Ombi.Core.Engine
{
    public class TvSearchEngine : BaseMediaEngine, ITvSearchEngine
    {
        public TvSearchEngine(IPrincipal identity, IRequestServiceMain service, ITvMazeApi tvMaze, IMapper mapper,
            ITraktApi trakt, IRuleEvaluator r, OmbiUserManager um,
            ICacheService memCache, ISettingsService<OmbiSettings> s, IRepository<RequestSubscription> sub)
            : base(identity, service, r, um, memCache, s, sub)
        {
            TvMazeApi = tvMaze;
            Mapper = mapper;
            TraktApi = trakt;
        }

        protected ITvMazeApi TvMazeApi { get; }
        protected IMapper Mapper { get; }
        private ITraktApi TraktApi { get; }

        public async Task<IEnumerable<SearchTvShowViewModel>> Search(string searchTerm)
        {
            var searchResult = await TvMazeApi.Search(searchTerm);

            if (searchResult != null)
            {
                var retVal = new List<SearchTvShowViewModel>();
                foreach (var tvMazeSearch in searchResult)
                {
                    if (tvMazeSearch.show.externals == null || !(tvMazeSearch.show.externals?.thetvdb.HasValue ?? false))
                    {
                        continue;
                    }
                    retVal.Add(await ProcessResult(tvMazeSearch));
                }
                return retVal;
            }
            return null;
        }

        public async Task<SearchTvShowViewModel> GetShowInformation(int tvdbid)
        {
            var show = await Cache.GetOrAdd(nameof(GetShowInformation) + tvdbid,
                async () => await TvMazeApi.ShowLookupByTheTvDbId(tvdbid), DateTime.Now.AddHours(12));
            if (show == null)
            { 
                // We don't have enough information
                return null;
            }

            var episodes = await Cache.GetOrAdd("TvMazeEpisodeLookup" + show.id,
                async () => await TvMazeApi.EpisodeLookup(show.id), DateTime.Now.AddHours(12));
            if (episodes == null || !episodes.Any())
            {
                // We don't have enough information
                return null;
            }

            var mapped = Mapper.Map<SearchTvShowViewModel>(show);

            foreach (var e in episodes)
            {
                var season = mapped.SeasonRequests.FirstOrDefault(x => x.SeasonNumber == e.season);
                if (season == null)
                {
                    var newSeason = new SeasonRequests
                    {
                        SeasonNumber = e.season,
                        Episodes = new List<EpisodeRequests>()
                    };
                    newSeason.Episodes.Add(new EpisodeRequests
                    {
                        Url = e.url,
                        Title = e.name,
                        AirDate = e.airstamp.HasValue() ? DateTime.Parse(e.airstamp) : DateTime.MinValue,
                        EpisodeNumber = e.number,

                    });
                    mapped.SeasonRequests.Add(newSeason);
                }
                else
                {
                    // We already have the season, so just add the episode
                    season.Episodes.Add(new EpisodeRequests
                    {
                        Url = e.url,
                        Title = e.name,
                        AirDate = e.airstamp.HasValue() ? DateTime.Parse(e.airstamp) : DateTime.MinValue,
                        EpisodeNumber = e.number,
                    });
                }
            }
            return await ProcessResult(mapped);
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Popular()
        {
            var result = await Cache.GetOrAdd(CacheKeys.PopularTv, async () => await TraktApi.GetPopularShows(null, ResultLimit), DateTime.Now.AddHours(12));
            var processed = ProcessResults(result);
            return await processed;
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Popular(int currentlyLoaded, int amountToLoad)
        {
            var pages = PaginationHelper.GetNextPages(currentlyLoaded, amountToLoad, ResultLimit);
            var results = new List<TraktShow>();
            foreach (var pagesToLoad in pages)
            {
                var apiResult = await Cache.GetOrAdd(nameof(Popular) + pagesToLoad.Page,
                    async () => await TraktApi.GetPopularShows(pagesToLoad.Page, ResultLimit), DateTime.Now.AddHours(12));
                results.AddRange(apiResult.Skip(pagesToLoad.Skip).Take(pagesToLoad.Take));
            }
            var processed = ProcessResults(results);
            return await processed;
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Anticipated()
        {

            var result = await Cache.GetOrAdd(CacheKeys.AnticipatedTv, async () => await TraktApi.GetAnticipatedShows(null, ResultLimit), DateTime.Now.AddHours(12));
            var processed = ProcessResults(result);
            return await processed;
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Anticipated(int currentlyLoaded, int amountToLoad)
        {
            var pages = PaginationHelper.GetNextPages(currentlyLoaded, amountToLoad, ResultLimit);
            var results = new List<TraktShow>();
            foreach (var pagesToLoad in pages)
            { 
                var apiResult = await Cache.GetOrAdd(nameof(Anticipated) + pagesToLoad.Page,
                    async () => await TraktApi.GetAnticipatedShows(pagesToLoad.Page, ResultLimit), DateTime.Now.AddHours(12));
                results.AddRange(apiResult.Skip(pagesToLoad.Skip).Take(pagesToLoad.Take));
            }
            var processed = ProcessResults(results);
            return await processed;
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Trending()
        {
            var result = await Cache.GetOrAdd(CacheKeys.TrendingTv, async () => await TraktApi.GetTrendingShows(null, ResultLimit), DateTime.Now.AddHours(12));
            var processed = ProcessResults(result);
            return await processed;
        }


        public async Task<IEnumerable<SearchTvShowViewModel>> Trending(int currentlyLoaded, int amountToLoad)
        {
            var pages = PaginationHelper.GetNextPages(currentlyLoaded, amountToLoad, ResultLimit);
            var results = new List<TraktShow>();
            foreach (var pagesToLoad in pages)
            {
                var apiResult = await Cache.GetOrAdd(nameof(Trending) + pagesToLoad.Page,
                    async () => await TraktApi.GetTrendingShows(pagesToLoad.Page, ResultLimit), DateTime.Now.AddHours(12));
                results.AddRange(apiResult.Skip(pagesToLoad.Skip).Take(pagesToLoad.Take));
            }
            var processed = ProcessResults(results);
            return await processed;
        }

        protected async Task<IEnumerable<SearchTvShowViewModel>> ProcessResults<T>(IEnumerable<T> items)
        {
            var retVal = new List<SearchTvShowViewModel>();
            foreach (var tvMazeSearch in items)
            {
                retVal.Add(await ProcessResult(tvMazeSearch));
            }
            return retVal;
        }

        protected async Task<SearchTvShowViewModel> ProcessResult<T>(T tvMazeSearch)
        {
            var mapped = Mapper.Map<SearchTvShowViewModel>(tvMazeSearch);
            return await ProcessResult(mapped);
        }

        private async Task<SearchTvShowViewModel> ProcessResult(SearchTvShowViewModel item)
        {
            item.TheTvDbId = item.Id.ToString();
            
            await RunSearchRules(item);

            return item;
        }
    }
}