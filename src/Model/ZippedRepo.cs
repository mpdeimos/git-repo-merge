﻿using System;
using System.Linq;
using LibGit2Sharp;
using System.Collections.Generic;
using Mpdeimos.GitRepoZipper.Util;
using System.Text.RegularExpressions;

namespace Mpdeimos.GitRepoZipper.Model
{
	/// <summary>
	/// Abstraction of a zipped repository.
	/// </summary>
	public class ZippedRepo
	{
		/// <summary>
		/// The known commits of the zipped repository.
		/// </summary>
		private HashSet<Commit> Commits = new HashSet<Commit>();

		/// <summary>
		/// List of all merge commits.
		/// </summary>
		private List<Commit> Merges = new List<Commit>();

		/// <summary>
		/// The named branches in the zipped repository.
		/// </summary>
		private Dictionary<string, ZippedBranch> Branches = new Dictionary<string, ZippedBranch>();

		/// <summary>
		/// Constructor
		/// </summary>
		public ZippedRepo(IEnumerable<Repository> repositories, Config config = null)
		{
			config = config ?? new Config();

			foreach (var repo in repositories)
			{
				Commit commonRoot = null;
				foreach (var branch in repo.Branches.Where(config.IsBranchIncluded).OrderBy(b => b.FriendlyName, new BranchComparer(config)))
				{
					List<Commit> commits = RecordBranch(branch.FriendlyName, branch);
					if (commonRoot == null)
					{
						commonRoot = commits.First();
					}
					if (commits.First() != commonRoot)
					{
						throw new ZipperException("Cannot zip repositories with multiple roots: " + repo.Info.Path + " branch: " + branch.FriendlyName);
					}
				}
			}
		}

		/// <summary>
		/// Adds a branch to the zipped repository. Returns the commits in oldes-to-newest order.
		/// </summary>
		public List<Commit> RecordBranch(string name, Branch branch)
		{
			if (!this.Branches.ContainsKey(name))
			{
				this.Branches[name] = new ZippedBranch(name);
			}

			var zippedBranch = this.Branches[name].AddBranch(branch);
			RecordCommits(zippedBranch);

			return zippedBranch;
		}

		/// <summary>
		/// Records a list of commits.
		/// </summary>
		private void RecordCommits(IEnumerable<Commit> commits)
		{
			foreach (var commit in commits)
			{
				if (this.Commits.Contains(commit))
				{
					continue;
				}

				this.Commits.Add(commit);
				if (commit.Parents.Count() > 1)
				{
					this.Merges.Add(commit);
					foreach (var parent in commit.Parents.Skip(1))
					{
						this.RecordCommits(RepoUtil.GetPrimaryParents(parent));
					}
				}
			}
		}

		public IEnumerable<string> GetBranches()
		{
			return this.Branches.Keys;
		}

		public IEnumerable<Commit> GetBranch(string name)
		{
			return this.Branches[name].GetZippedBranch();
		}

		// TODO Test
		public IEnumerable<Commit> GetAnonymousBranchCommits()
		{
			var mergeParents = new HashSet<Commit>(this.Merges.SelectMany(merge => merge.Parents));
			foreach (var branchCommit in this.Branches.SelectMany(entry => entry.Value.GetZippedBranch()))
			{
				mergeParents.Remove(branchCommit);
			}

			return mergeParents;
		}

		public IEnumerable<Commit> GetMerges()
		{
			return this.Merges;
		}

		public class BranchComparer : Comparer<string>
		{
			private Config config;

			public BranchComparer(Config config)
			{
				this.config = config;
			}

			public override int Compare(string x, string y)
			{
				int comp = GetMatchingIncludeOrdinal(x).CompareTo(GetMatchingIncludeOrdinal(y));
				if (comp != 0)
				{
					return comp;
				}

				return x.CompareTo(y);
			}

			private int GetMatchingIncludeOrdinal(string x)
			{
				return this.config.Include?.TakeWhile(pattern => !Regex.IsMatch(x, pattern)).Count() ?? 0;
			}
		}
	}
}

