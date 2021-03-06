﻿using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fbeltrao.AzureFunctionExtensions
{
    /// <summary>
    /// Collector for <see cref="RedisItem"/>    
    /// </summary>
    public class RedisItemAsyncCollector : IAsyncCollector<RedisItem>
    {
        readonly RedisExtensionConfigProvider config;
        readonly RedisAttribute attr;
        readonly IRedisDatabaseManager redisDatabaseManager;
        readonly List<RedisItem> redisItemCollection;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        /// <param name="attr"></param>
        public RedisItemAsyncCollector(RedisExtensionConfigProvider config, RedisAttribute attr) : this(config, attr, RedisDatabaseManager.GetInstance())
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        /// <param name="attr"></param>
        public RedisItemAsyncCollector(RedisExtensionConfigProvider config, RedisAttribute attr, IRedisDatabaseManager redisDatabaseManager)
        {
            this.config = config;
            this.attr = attr;
            this.redisDatabaseManager = redisDatabaseManager;
            this.redisItemCollection = new List<RedisItem>();
        }

        /// <summary>
        /// Adds item to collection to be sent to redis
        /// </summary>
        /// <param name="item"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task AddAsync(RedisItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            // create item based on the item + attribute + configuration
            var finalItem = new RedisItem()
            {
                BinaryValue = item.BinaryValue,
                ObjectValue = item.ObjectValue,
                TextValue = item.TextValue,                
                Key = Utils.MergeValueForProperty(item.Key, attr.Key, config.Key),
                TimeToLive = Utils.MergeValueForNullableProperty<TimeSpan>(item.TimeToLive, attr.TimeToLive, config.TimeToLive),
                IncrementValue = item.IncrementValue,
                RedisItemOperation = item.RedisItemOperation
            };

            if (finalItem.RedisItemOperation == RedisItemOperation.NotSet)
            {
                if (attr.RedisItemOperation != RedisItemOperation.NotSet)
                    finalItem.RedisItemOperation = attr.RedisItemOperation;
                else
                    finalItem.RedisItemOperation = config.RedisItemOperation;
            }



            if (this.config.SendInBatch)
            {
                this.redisItemCollection.Add(finalItem);
            }
            else
            {
                await SendToRedis(finalItem);
            }            
        }

        /// <summary>
        /// Flushs all items to redis
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var item in this.redisItemCollection)
            {
                await SendToRedis(item);

                if (cancellationToken != null && cancellationToken.IsCancellationRequested)
                    break;
            }
        }

        /// <summary>
        /// Sends <see cref="RedisItem"/> to Redis
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        async Task SendToRedis(RedisItem item)
        {
            var connectionString = Utils.MergeValueForProperty(attr.Connection, config.Connection);            
            var db = redisDatabaseManager.GetDatabase(connectionString); // TODO: add support for multiple databases

            RedisValue value = CreateRedisValue(item);

            switch (item.RedisItemOperation)
            {
                case RedisItemOperation.SetKeyValue:
                    {
                        await db.StringSetAsync(item.Key, value, item.TimeToLive, When.Always, CommandFlags.FireAndForget);
                        break;
                    }

                case RedisItemOperation.IncrementValue:
                    {
                        await db.StringIncrementAsync(item.Key, item.IncrementValue);
                        break;
                    }

                case RedisItemOperation.ListRightPush:
                    {
                        await db.ListRightPushAsync(item.Key, value, When.Always, CommandFlags.FireAndForget);
                        break;
                    }

                case RedisItemOperation.ListLeftPush:
                    {
                        await db.ListLeftPushAsync(item.Key, value, When.Always, CommandFlags.FireAndForget);
                        break;
                    }
            }
        }

        /// <summary>
        /// Helper method that creates a <see cref="RedisValue"/> based on <see cref="RedisItem"/>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private RedisValue CreateRedisValue(RedisItem item)
        {
            RedisValue returnValue = RedisValue.Null;
            if (item.BinaryValue != null)
            {
                returnValue = item.BinaryValue;
            }
            else if (item.ObjectValue != null)
            {
                returnValue = JsonConvert.SerializeObject(item.ObjectValue);
            }
            else
            {
                returnValue = item.TextValue;
            }

            return returnValue;
        }
    }
}
