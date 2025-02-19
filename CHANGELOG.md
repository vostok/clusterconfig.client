## 0.2.30 (19-02-2025):

V3_1 protocol (see changes for 0.2.23) is now used by default.

## 0.2.29 (31-01-2025):

Fix a race between two Tasks: one from first initial observable subscribing and second from propagating new state for just subscribed observers.
Fix a bug with handling empty patches.

## 0.2.27 (16-12-2024): 

Bump NuGet deps versions

## 0.2.26 (09-12-2024):

V2 protocol is now used by default due to temporary backend instability under heavy load. 

## 0.2.25 (09-12-2024):

Bugfix: When server recommended downgrading Procotol, it couldn't work until the version changed at least once.

## 0.2.24 (09-12-2024):

V3 protocol (see changes for 0.2.23) is new used by default.

## 0.2.23 (20-11-2024):

Add support for the new V3 protocol. This protocol allows working not with the full tree, but only with the required subtrees. All code is adapted to this feature, but the default protocol is still V2. V3 can be enabled in `ClusterConfigClientSettings.ForcedProtocolVersion`.

Breaking changes ror V3: Not first access to settings (but the first access to required subtree) can now go to the backend and download settings. And can even fail with an exception. In version 2, the first access to any settings downloaded the entire tree and was always served from the cache.

## 0.2.22 (20-11-2024):

Update ClusterClient libraries

## 0.2.20 (05-08-2023):

Add options to intern string values after deserialization

## 0.2.18 (23-09-2022):

Fix: treat `TryAgain` SocketError code as empty cluster.

## 0.2.17 (22-03-2022):

Add trees descriptions into log messages

## 0.2.16 (28-02-2022):

Change defult protocol to V2

## 0.2.15 (04-02-2022):

Fix versions order

## 0.2.0 (04-02-2022):

Added protocol V2

## 0.1.18 (13-01-2022):

Reduced memory traffic and added several optimizations to the hot pathes.

## 0.1.17 (06-12-2021):

Added `net6.0` target.

## 0.1.15 (20.10.2021):

- Use remote version when local settings are disabled.

## 0.1.14 (28.09.2021):

Fixed #4.

## 0.1.13 (01.07.2021):

Added `MergeOptions` setting.

## 0.1.12 (25.06.2021):

Fixed https://github.com/vostok/clusterconfig.client/issues/9

## 0.1.11 (23.06.2021):

- Added new `AssumeClusterConfigDeployed` setting to remove wrong assumptions in some cases.

## 0.1.10 (24.02.2021):

Whole file parser for .md files.

## 0.1.9 (02.08.2020):

- Marked initial update requests with critical priority.
- Added retry to initial update requests.

## 0.1.8 (04.03.2020):

Updated dependencies.

## 0.1.7 (16.10.2019):

Added `TrySetDefaultClient` method.

## 0.1.6 (05.10.2019):

Local settings reader no longer attempts to parse `.bin` files.

## 0.1.5 (07.09.2019):

Local settings reader no longer attempts to parse `.yml`, `.yaml` and `.toml` files.

## 0.1.4 (03.08.2019):

Fixed https://github.com/vostok/clusterconfig.client/issues/2

## 0.1.3 (24.07.2019):

Folder for default settings is now searched up to 10 levels outwards from current directory (up from 3).

## 0.1.1 (24.04.2019):

Fixed https://github.com/vostok/clusterconfig.client/issues/1

## 0.1.0 (18-02-2019): 

Initial prerelease.