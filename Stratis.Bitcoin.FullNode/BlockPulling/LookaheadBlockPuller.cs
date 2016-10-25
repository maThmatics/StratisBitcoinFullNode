﻿using NBitcoin;
using NBitcoin.Protocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stratis.Bitcoin.FullNode.BlockPulling
{
	public abstract class LookaheadBlockPuller : BlockPuller
	{
		class DownloadedBlock
		{
			public int Length;
			public Block Block;
		}
		const int BLOCK_SIZE = 2000000;
		public LookaheadBlockPuller()
		{
			MaxBufferedSize = BLOCK_SIZE * 10;
			Lookahead = 5;
		}

		protected int Lookahead
		{
			get;
			set;
		}

		public ChainedBlock Location
		{
			get
			{
				return _Location;
			}
		}

		int _CurrentDownloading = 0;

		public int MaxBufferedSize
		{
			get;
			set;
		}


		public bool Stalling
		{
			get;
			internal set;
		}

		long _CurrentSize;
		ConcurrentDictionary<uint256, DownloadedBlock> _DownloadedBlocks = new ConcurrentDictionary<uint256, DownloadedBlock>();

		ConcurrentChain _Chain;
		ChainedBlock _Location;
		ChainedBlock _LookaheadLocation;

		public override void SetLocation(ChainedBlock tip)
		{
			if(tip == null)
				throw new ArgumentNullException("tip");
			_Location = tip;
		}

		public ConcurrentChain Chain
		{
			get
			{
				return _Chain;
			}
		}

		public override Block NextBlock()
		{
			if(_Chain == null)
				ReloadChain();
			if(_LookaheadLocation == null)
			{
				AskBlocks();
				AskBlocks();
			}
			var block = NextBlockCore();
			if((_LookaheadLocation.Height - _Location.Height) <= Lookahead)
				AskBlocks();
			return block;
		}		

		public override void Reject(Block block, RejectionMode rejectionMode)
		{
			var h = block.GetHash();
			if(_Chain.Contains(h))
				ReloadChain();
			_RejectedHashes.TryAdd(h, h);
		}

		ConcurrentDictionary<uint256, uint256> _RejectedHashes = new ConcurrentDictionary<uint256, uint256>();
		public bool IsRejected(uint256 blockHash)
		{
			return _RejectedHashes.ContainsKey(blockHash);
		}

		protected abstract void AskBlocks(ChainedBlock[] downloadRequests);
		protected abstract ConcurrentChain ReloadChainCore();
		private void ReloadChain()
		{
			lock(_ChainLock)
			{
				_Chain = ReloadChainCore();
			}
		}

		AutoResetEvent _Consumed = new AutoResetEvent(false);
		AutoResetEvent _Pushed = new AutoResetEvent(false);

		/// <summary>
		/// If true, the puller is a bottleneck
		/// </summary>
		public bool IsStalling
		{
			get;
			internal set;
		}

		/// <summary>
		/// If true, the puller consumer is a bottleneck
		/// </summary>
		public bool IsFull
		{
			get;
			internal set;
		}

		protected void PushBlock(int length, Block block)
		{
			var hash = block.Header.GetHash();
			var header = _Chain.GetBlock(hash);
			while(_CurrentSize + length >= MaxBufferedSize && header.Height != _Location.Height + 1)
			{
				IsFull = true;
				_Consumed.WaitOne(1000);
			}
			IsFull = false;
			_DownloadedBlocks.TryAdd(hash, new DownloadedBlock() { Block = block, Length = length });
			_CurrentSize += length;
			_Pushed.Set();
		}

		object _ChainLock = new object();

		private void AskBlocks()
		{
			if(_Location == null)
				throw new InvalidOperationException("SetLocation should have been called");
			if(_LookaheadLocation == null && !_Chain.Contains(_Location))
				return;
			if(_LookaheadLocation != null && !_Chain.Contains(_LookaheadLocation))
				_LookaheadLocation = null;

			ChainedBlock[] downloadRequests = null;
			lock(_ChainLock)
			{
				ChainedBlock lookaheadBlock = _LookaheadLocation ?? _Location;
				ChainedBlock nextLookaheadBlock = _Chain.GetBlock(Math.Min(lookaheadBlock.Height + Lookahead, _Chain.Height));
				_LookaheadLocation = nextLookaheadBlock;

				downloadRequests = new ChainedBlock[nextLookaheadBlock.Height - lookaheadBlock.Height];
				if(downloadRequests.Length == 0)
					return;
				for(int i = 0; i < downloadRequests.Length; i++)
				{
					downloadRequests[i] = _Chain.GetBlock(lookaheadBlock.Height + 1 + i);
				}
			}
			AskBlocks(downloadRequests);
		}

		private Block NextBlockCore()
		{
			while(true)
			{
				var header = _Chain.GetBlock(_Location.Height + 1);
				DownloadedBlock block;
				if(header != null && _DownloadedBlocks.TryRemove(header.HashBlock, out block))
				{
					IsStalling = false;
					_Location = header;
					Interlocked.Add(ref _CurrentSize, -block.Length);
					_Consumed.Set();
					return block.Block;
				}
				else
				{
					IsStalling = true;
					_Pushed.WaitOne(1000);
				}
			}
		}
	}
}