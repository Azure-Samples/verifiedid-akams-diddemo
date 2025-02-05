// callback methods from RequestService class
function renderQRCode(url) {
    document.getElementById('qrcode').style.display = "block";
    document.getElementById("qrcode").getElementsByTagName("img")[0].style.opacity = "1.0";
    qrcode.makeCode(url);
}
function dimQRCode() {
    document.getElementById("qrcode").getElementsByTagName("img")[0].style.opacity = "0.1";
}
function hideQRCode() {
    document.getElementById("qrcode").style.display = "none";
    document.getElementById("qrcode").getElementsByTagName("img")[0].style.display = "none";
    var pinCode = document.getElementById('pinCode');
    if ( null != pinCode ) {
        pinCode.style.display = "none";
    }
}
function drawQRCode(requestType, id, url, pinCode) {
    renderQRCode(url);
    if (requestType == "presentation") {
        document.getElementById('verify-credential').style.display = "none";
    } else if (requestType == "issuance") {
        if (pinCode != undefined) {
            document.getElementById('pinCode').innerHTML = "Pin code: " + pinCode;
            document.getElementById('pinWrapper').style.display = "block";
        }
    } else if (requestType == "selfie") {
        //displayMessage("Waiting for QR code to be scanned with QR code reader app");
    }
}
function showDialog(title, message) {
    document.getElementById("msgTitle").innerText = String(title).charAt(0).toUpperCase() + String(title).slice(1);
    document.getElementById("msgBody").innerText = message;
    document.getElementById("showDialog").click();
}

/*
function hideMessage() {
    document.getElementById("message-wrapper").style.display = "none";
    document.getElementById('message').innerHTML = "";
}
function displayMessage(msg) {
    document.getElementById("message-wrapper").style.display = "block";    
    document.getElementById('message').innerHTML = msg;
}
*/
function navigateToDeepLink(requestType, status, id, url) {
    requestService.pollRequestStatus(id);
    if (requestType == "selfie") {
        window.open(url, '_blank').focus();
    } else {
        window.open(url, '_blank').focus();
        //window.location.href = url;
    }
}

// method to post selfie taken before starting an issuance request
function setUserPhoto() {
    if ("none" != document.getElementById('selfie').style.display && document.getElementById('selfie').src != "") {
        photoId = requestService.setUserPhoto(document.getElementById('selfie').src);
    }
}

function hideShowPhotoElements(val) {
    document.getElementById("take-selfie").style.display = val;
    document.getElementById("imageUpload").style.display = val;
    document.getElementById("photo-help").style.display = val;
}

function uploadImage(e) {
    if (e.target.files) {
        var reader = new FileReader();
        reader.readAsDataURL(e.target.files[0]);
        reader.onloadend = function (e) {
            var imageObj = new Image();
            imageObj.src = e.target.result;
            imageObj.onload = function (ev) {
                var canvas = document.createElement("canvas");
                canvas.width = 480;
                canvas.height = 640;
                console.log(`img size: ${imageObj.naturalWidth} x ${imageObj.naturalHeight}`);
                canvas.getContext('2d').drawImage(imageObj, 0, 0, imageObj.naturalWidth, imageObj.naturalHeight, 0, 0, canvas.width, canvas.height);
                document.getElementById("selfie").src = canvas.toDataURL('image/jpeg');
                document.getElementById("selfie").style.display = "block";
            }
        }
    }
}

function requestCallback(requestType, event, id, data) {
    switch (event) {
        case "retrieved":
            dimQRCode();
            if (requestType == "presentation") {
                displayMessage("QR code scanned. Waiting for Verified ID credential to be shared from wallet...");
            } else {
                displayMessage("QR code scanned. Waiting for Verified ID credential to be added to wallet...");
            }
            break;
        case "verified":
            hideQRCode();
            displayMessage("Presentation received");
            break;
        case "issued":
            hideQRCode();
            displayMessage("Issuance completed");
            break;
        case "selfie_taken":
            hideQRCode();
            displayMessage("Selfie taken");
            document.getElementById('selfie').src = "data:image/jpeg;base64," + data.photo;
            document.getElementById('selfie').style.display = "block";
            break;
        case "cancelled": case "timeout": case "error":
            hideQRCode();
            showDialog(`${requestType} ${event}`, JSON.stringify(data));
            break;
    }
}


// RequestService object that drives the interaction with backend APIs
// verifiedid.requestservice.client.js
var requestService = new RequestService(drawQRCode,
    navigateToDeepLink,
    requestCallback, // retrieved
    requestCallback, // cancelled
    requestCallback, // verified
    requestCallback, // issued
    requestCallback, // selfieTaken
    requestCallback  // error
);
// If to do console.log (a lot)
requestService.logEnabled = true;

