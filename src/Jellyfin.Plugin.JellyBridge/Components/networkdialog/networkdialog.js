define(["dialogHelper","dom","jQuery"],function(dialogHelper,dom,$){
    "use strict";
    function showNetworkDialog(network, context, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open("GET", "Components/networkdialog/networkdialog.template.html", true);
        xhr.onload = function() {
            var template = this.response;
            var dlg = dialogHelper.createDialog({size:"small",modal:true,removeOnClose:true,scrollY:false});
            dlg.classList.add("formDialog");
            dlg.innerHTML = template;
            dlg.querySelector("#networkName").textContent = network.name;
            dlg.querySelector("#networkId").textContent = network.id;
            dlg.querySelector("#networkRegion").textContent = network.region;
            var actionBtn = dlg.querySelector(".btnAction");
            if (context === 'available') {
                actionBtn.textContent = 'Add to Selected Networks';
                actionBtn.onclick = function() {
                    dialogHelper.close(dlg);
                    if (callback) callback('add');
                };
            } else if (context === 'active') {
                actionBtn.textContent = 'Remove from Selected Networks';
                actionBtn.onclick = function() {
                    dialogHelper.close(dlg);
                    if (callback) callback('remove');
                };
            }
            dlg.querySelector(".btnCancel").addEventListener("click", function() { dialogHelper.close(dlg); });
            dlg.querySelector(".btnClose").addEventListener("click", function() { dialogHelper.close(dlg); });
            dialogHelper.open(dlg);
        };
        xhr.send();
    }
    return { showNetworkDialog: showNetworkDialog };
});
