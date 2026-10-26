# Third-party components

JustVID uses the following dynamically linked components. Their license notices in the runtime distribution must be retained when redistributing.

- LibVLCSharp 3.9.3 and LibVLCSharp.WPF 3.9.3: LGPL-2.1-or-later. Source, license and build information: https://github.com/videolan/libvlcsharp/tree/3.9.3
- VideoLAN.LibVLC.Windows 3.0.21: LibVLC runtime and codec plugins. The VideoLAN distribution includes components with their own licenses. LibVLC source and license: https://code.videolan.org/videolan/vlc/-/tree/3.0.21 ; corresponding VLC source archive: https://download.videolan.org/pub/videolan/vlc/3.0.21/vlc-3.0.21.tar.xz

The LibVLC DLLs and plugins are shipped as separate files under libvlc/win-x64 so they can be replaced independently of JustVID. JustVID does not modify their source code. This file does not replace the components' original license texts.
