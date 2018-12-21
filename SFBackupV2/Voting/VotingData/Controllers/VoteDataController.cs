using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace VotingData.Controllers
{
    [Route("api/[controller]")]
    public class VoteDataController : Controller
    {
        private readonly IReliableStateManager stateManager;

        public VoteDataController(IReliableStateManager stateManager)
        {
            // The ReliableStateManager handles the processing of the reliable dictionaries and queues
            // It is typically a part of the context in the VotingData Stateful service class, but for the 
            // Controller, the ReliableStateManager needs to be passed in to the constructor to be used
            // Look at the code for the Kestrel communications listener if the VotingData class to see how
            // the ReliableStateManager is added
            this.stateManager = stateManager;
        }

        // GET api/VoteData
        /// <summary>
        /// Get a list of votes
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            // make sure to honor the cancellation token so that when an event occurs within Service Fabric 
            // that may shut down or move the stateful service instance, the method will exit gracefully
            var ct = new CancellationToken();

            var votesDictionary = await this.stateManager.GetOrAddAsync<IReliableDictionary<string, int>>("counts");

            // Any operation on a reliable dictionary or queue can only be done within the context of a transaction
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                var list = await votesDictionary.CreateEnumerableAsync(tx);

                var enumerator = list.GetAsyncEnumerator();

                var result = new List<KeyValuePair<string, int>>();

                while (await enumerator.MoveNextAsync(ct))
                {
                    result.Add(enumerator.Current);
                }

                return Json(result);
            }
        }

        // PUT api/VoteData/name
        /// <summary>
        /// Adds a vote to the dictionary
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [HttpPut("{name}")]
        public async Task<IActionResult> Put(string name)
        {
            var votesDictionary = await this.stateManager.GetOrAddAsync<IReliableDictionary<string, int>>("counts");

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await votesDictionary.AddOrUpdateAsync(tx, name, 1, (key, oldvalue) => oldvalue + 1);
                await tx.CommitAsync();
            }

            return new OkResult();
        }

        // DELETE api/VoteData/name
        /// <summary>
        /// Deletes a vote from the dictionary
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [HttpDelete("{name}")]
        public async Task<IActionResult> Delete(string name)
        {
            var votesDictionary = await this.stateManager.GetOrAddAsync<IReliableDictionary<string, int>>("counts");

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                if (await votesDictionary.ContainsKeyAsync(tx, name))
                {
                    await votesDictionary.TryRemoveAsync(tx, name);
                    await tx.CommitAsync();
                    return new OkResult();
                }
                else
                {
                    return new NotFoundResult();
                }
            }
        }

    }
}
