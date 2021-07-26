﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Construction;
using Microsoft.Build.Shared;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.Build.Evaluation
{
    internal partial class LazyItemEvaluator<P, I, M, D>
    {
        class RemoveOperation : LazyItemOperation
        {
            readonly ImmutableList<string> _matchOnMetadata;
            private MetadataTrie<P, I> _metadataSet;

            public RemoveOperation(RemoveOperationBuilder builder, LazyItemEvaluator<P, I, M, D> lazyEvaluator)
                : base(builder, lazyEvaluator)
            {
                _matchOnMetadata = builder.MatchOnMetadata.ToImmutable();

                ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile(
                    _matchOnMetadata.IsEmpty || _itemSpec.Fragments.All(f => f is ItemSpec<ProjectProperty, ProjectItem>.ItemExpressionFragment),
                    new BuildEventFileInfo(string.Empty),
                    "OM_MatchOnMetadataIsRestrictedToReferencedItems");

                if (!_matchOnMetadata.IsEmpty)
                {
                    _metadataSet = new MetadataTrie<P, I>(builder.MatchOnMetadataOptions, _matchOnMetadata, _itemSpec);
                }
            }

            /// <summary>
            /// Apply the Remove operation.
            /// </summary>
            /// <remarks>
            /// This operation is mostly implemented in terms of the default <see cref="LazyItemOperation.ApplyImpl(OrderedItemDataCollection.Builder, ImmutableHashSet{string})"/>.
            /// This override exists to apply the removing-everything short-circuit.
            /// </remarks>
            protected override void ApplyImpl(OrderedItemDataCollection.Builder listBuilder, ImmutableHashSet<string> globsToIgnore)
            {
                if (_matchOnMetadata.IsEmpty && ItemspecContainsASingleBareItemReference(_itemSpec, _itemElement.ItemType) && _conditionResult)
                {
                    // Perf optimization: If the Remove operation references itself (e.g. <I Remove="@(I)"/>)
                    // then all items are removed and matching is not necessary
                    listBuilder.Clear();
                    return;
                }

                base.ApplyImpl(listBuilder, globsToIgnore);
            }

            // todo Perf: do not match against the globs: https://github.com/Microsoft/msbuild/issues/2329
            protected override ImmutableList<I> SelectItems(OrderedItemDataCollection.Builder listBuilder, ImmutableHashSet<string> globsToIgnore)
            {
                var items = ImmutableHashSet.CreateBuilder<I>();
                if (_matchOnMetadata.IsEmpty)
                {
                    foreach (I item in _itemSpec.GetMatchesByPathComparison(listBuilder.Dictionary))
                    {
                        items.Add(item);
                    }
                }
                else
                {
                    foreach (ItemData item in listBuilder)
                    {
                        if (MatchesItemOnMetadata(item.Item))
                        {
                            items.Add(item.Item);
                        }
                    }
                }

                return items.ToImmutableList();
            }

            private bool MatchesItemOnMetadata(I item)
            {
                return _metadataSet.Contains(_matchOnMetadata.Select(m => item.GetMetadataValue(m)));
            }

            protected override void SaveItems(ImmutableList<I> items, OrderedItemDataCollection.Builder listBuilder)
            {
                if (!_conditionResult)
                {
                    return;
                }

                listBuilder.RemoveAll(item => items.Contains(item));
            }

            public ImmutableHashSet<string>.Builder GetRemovedGlobs()
            {
                var builder = ImmutableHashSet.CreateBuilder<string>();

                if (!_conditionResult)
                {
                    return builder;
                }

                var globs = _itemSpec.Fragments.OfType<GlobFragment>().Select(g => g.TextFragment);

                builder.UnionWith(globs);

                return builder;
            }
        }

        class RemoveOperationBuilder : OperationBuilder
        {
            public ImmutableList<string>.Builder MatchOnMetadata { get; } = ImmutableList.CreateBuilder<string>();

            public MatchOnMetadataOptions MatchOnMetadataOptions { get; set; }

            public RemoveOperationBuilder(ProjectItemElement itemElement, bool conditionResult) : base(itemElement, conditionResult)
            {
            }
        }
    }
}
