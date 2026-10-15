// SPDX-License-Identifier: GPL-3.0-or-later
// Model data adapted from LibrePods. Copyright (C) 2025 LibrePods contributors.
// See docs/UPSTREAM_MAP.md for exact upstream file and commit.
using System.Collections.Immutable;

namespace AirPodsSwitch.Core.Protocol;

public sealed record AirPodsModel(string Name, ImmutableArray<string> Capabilities);

public static class AirPodsModels
{
    private static readonly ImmutableArray<string> Shared =
        ["ListeningMode", "ConversationAwareness", "HeadGestures", "AdaptiveAudio",
         "SleepDetection", "AdaptiveVolume", "StemConfig"];
    public static AirPodsModel? Find(string model) => model switch
    {
        "A3531" or "A3532" or "A3533" => new("AirPods 5", Shared),
        "A3439" or "A3440" or "A3441" => new("AirPods 5 with Wireless Charging Case", Shared.Add("SwipeForVolume")),
        _ => null
    };
}
