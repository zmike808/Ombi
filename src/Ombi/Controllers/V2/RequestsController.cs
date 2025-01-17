﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.Threading.Tasks;

using Ombi.Core;
using Ombi.Core.Engine;
using Ombi.Core.Engine.Interfaces;
using Ombi.Core.Models.Requests;
using Ombi.Core.Models.UI;
using Ombi.Store.Entities.Requests;

namespace Ombi.Controllers.V2
{
    public class RequestsController : V2Controller
    {
        public RequestsController(IMovieRequestEngine movieRequestEngine, ITvRequestEngine tvRequestEngine)
        {
            _movieRequestEngine = movieRequestEngine;
            _tvRequestEngine = tvRequestEngine;
        }

        private readonly IMovieRequestEngine _movieRequestEngine;
        private readonly ITvRequestEngine _tvRequestEngine;

        /// <summary>
        /// Gets movie requests.
        /// </summary>
        /// <param name="count">The count of items you want to return. e.g. 30</param>
        /// <param name="position">The position. e.g. position 60 for a 2nd page (since we have already got the first 30 items)</param>
        /// <param name="sort">The item to sort on e.g. "requestDate"</param>
        /// <param name="sortOrder">asc or desc</param>
        [HttpGet("movie/{count:int}/{position:int}/{sort}/{sortOrder}")]
        public async Task<RequestsViewModel<MovieRequests>> GetRequests(int count, int position, string sort, string sortOrder)
        {
            return await _movieRequestEngine.GetRequests(count, position, sort, sortOrder);
        }

        /// <summary>
        /// Gets the unavailable movie requests.
        /// </summary>
        /// <param name="count">The count of items you want to return. e.g. 30</param>
        /// <param name="position">The position. e.g. position 60 for a 2nd page (since we have already got the first 30 items)</param>
        /// <param name="sort">The item to sort on e.g. "requestDate"</param>
        /// <param name="sortOrder">asc or desc</param>
        [HttpGet("movie/unavailable/{count:int}/{position:int}/{sort}/{sortOrder}")]
        public async Task<RequestsViewModel<MovieRequests>> GetNotAvailableRequests(int count, int position, string sort, string sortOrder)
        {
            return await _movieRequestEngine.GetUnavailableRequests(count, position, sort, sortOrder);
        }

        /// <summary>
        /// Gets Tv requests.
        /// </summary>
        /// <param name="count">The count of items you want to return. e.g. 30</param>
        /// <param name="position">The position. e.g. position 60 for a 2nd page (since we have already got the first 30 items)</param>
        /// <param name="sort">The item to sort on e.g. "requestDate"</param>
        /// <param name="sortOrder">asc or desc</param>
        [HttpGet("tv/{count:int}/{position:int}/{sort}/{sortOrder}")]
        public async Task<RequestsViewModel<ChildRequests>> GetTvRequests(int count, int position, string sort, string sortOrder)
        {
            return await _tvRequestEngine.GetRequests(count, position, sort, sortOrder);
        }

        /// <summary>
        /// Gets unavailable Tv requests.
        /// </summary>
        /// <param name="count">The count of items you want to return. e.g. 30</param>
        /// <param name="position">The position. e.g. position 60 for a 2nd page (since we have already got the first 30 items)</param>
        /// <param name="sort">The item to sort on e.g. "requestDate"</param>
        /// <param name="sortOrder">asc or desc</param>
        [HttpGet("tv/unavailable/{count:int}/{position:int}/{sort}/{sortOrder}")]
        public async Task<RequestsViewModel<ChildRequests>> GetNotAvailableTvRequests(int count, int position, string sort, string sortOrder)
        {
            return await _tvRequestEngine.GetUnavailableRequests(count, position, sort, sortOrder);
        }

        [HttpPost("movie/advancedoptions")]
        public async Task<RequestEngineResult> UpdateAdvancedOptions([FromBody] MovieAdvancedOptions options)
        {
            return await _movieRequestEngine.UpdateAdvancedOptions(options);
        }
    }
}