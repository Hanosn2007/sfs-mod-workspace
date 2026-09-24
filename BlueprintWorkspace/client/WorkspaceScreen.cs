using SFS.Input;
using UnityEngine;

namespace BlueprintWorkspace
{
    // Opening this screen removes Screen_Game from the game's touch targets.
    // The build grid therefore cannot receive the same drag as our menu.
    internal sealed class WorkspaceScreen : Screen_Menu
    {
        protected override CloseMode OnEscape => CloseMode.Current;

        public override void OnOpen() { }

        public override void OnClose() { }
    }
}
